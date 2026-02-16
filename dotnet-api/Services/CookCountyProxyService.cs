using System.Net;
using System.Text.RegularExpressions;
using System.Web;
using HtmlAgilityPack;
using Microsoft.Extensions.Caching.Memory;
using CookCountyApi.Models;

namespace CookCountyApi.Services;

public interface ICookCountyProxyService
{
    Task<object> FetchTaxPortalAsync(string pin);
    Task<object> FetchCountyClerkAsync(string pin);
    Task<object> FetchTreasurerAsync(string pin);
    Task<object> FetchRecorderAsync(string pin);
    Task<Dictionary<string, string>> FetchRecorderConsiderationsAsync(string pin, List<(string docNumber, string viewUrl)> documents);
    Task<object> FetchAssessorAsync(string pin);
    void ClearCache(string? pin = null);
}

public class CookCountyProxyService : ICookCountyProxyService
{
    private readonly IMemoryCache _cache;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CookCountyProxyService> _logger;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);
    private static readonly Regex PinRegex = new(@"^\d{2}-\d{2}-\d{3}-\d{3}-\d{4}$", RegexOptions.Compiled);
    
    private const string TaxPortalBaseUrl = "https://www.cookcountypropertyinfo.com";

    public CookCountyProxyService(IMemoryCache cache, IHttpClientFactory httpClientFactory, ILogger<CookCountyProxyService> logger)
    {
        _cache = cache;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    private static bool ValidatePin(string pin) => !string.IsNullOrEmpty(pin) && PinRegex.IsMatch(pin);

    private static ApiErrorResponse Error(string message, string code) => new() { Error = message, Code = code };

    /// <summary>
    /// Tab 1: Tax Portal - fetches from cookcountypropertyinfo.com directly
    /// Shows tax bill history, property characteristics, and billed amounts
    /// Uses ASP.NET WebForms form submission with __VIEWSTATE tokens
    /// </summary>
    public async Task<object> FetchTaxPortalAsync(string pin)
    {
        if (!ValidatePin(pin))
        {
            return Error("Invalid or missing PIN. Format: XX-XX-XXX-XXX-XXXX", "INVALID_PIN");
        }

        var cacheKey = $"taxportal_{pin}";
        if (_cache.TryGetValue(cacheKey, out string? cachedHtml) && cachedHtml != null)
        {
            return new ApiSuccessResponse<TaxPortalData>
            {
                Data = new TaxPortalData
                {
                    Html = cachedHtml,
                    Pin = pin,
                    FetchedAt = DateTime.UtcNow.ToString("o")
                }
            };
        }

        try
        {
            // Use a handler with cookies to maintain session
            var handler = new HttpClientHandler
            {
                CookieContainer = new CookieContainer(),
                UseCookies = true,
                AllowAutoRedirect = true
            };
            
            using var client = new HttpClient(handler);
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
            client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.5");
            
            // Step 1: GET the default page to extract ASP.NET state tokens
            _logger.LogInformation("Fetching default page to get form tokens for PIN {Pin}", pin);
            var getResponse = await client.GetAsync($"{TaxPortalBaseUrl}/default.aspx");
            
            if (!getResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to get default page: {Status}", getResponse.StatusCode);
                return Error("Failed to access property portal", "FETCH_ERROR");
            }
            
            var initialHtml = await getResponse.Content.ReadAsStringAsync();
            
            // Extract ASP.NET state tokens
            var viewState = ExtractFormValue(initialHtml, "__VIEWSTATE");
            var viewStateGenerator = ExtractFormValue(initialHtml, "__VIEWSTATEGENERATOR");
            var previousPage = ExtractFormValue(initialHtml, "__PREVIOUSPAGE");
            var eventValidation = ExtractFormValue(initialHtml, "__EVENTVALIDATION");
            
            if (string.IsNullOrEmpty(viewState) || string.IsNullOrEmpty(eventValidation))
            {
                _logger.LogWarning("Could not extract form tokens from default page");
                return Error("Failed to initialize property search", "FETCH_ERROR");
            }
            
            // Split PIN into parts: 16-06-310-022-0000 -> [16, 06, 310, 022, 0000]
            var pinParts = pin.Split('-');
            
            // Step 2: POST the form with PIN search - matching PHP proxy approach exactly
            // Key insight: The reCAPTCHA is only enforced client-side, server accepts empty token
            var formContent = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__LASTFOCUS"] = "",
                ["__EVENTTARGET"] = "",
                ["__EVENTARGUMENT"] = "btnPIN",
                ["__VIEWSTATE"] = viewState,
                ["__VIEWSTATEGENERATOR"] = viewStateGenerator,
                ["__PREVIOUSPAGE"] = previousPage ?? "",
                ["__EVENTVALIDATION"] = eventValidation,
                ["ctl00$PINAddressSearch2$pin2Box1"] = "",
                ["ctl00$PINAddressSearch2$pin2Box2"] = "",
                ["ctl00$PINAddressSearch2$pin2Box3"] = "",
                ["ctl00$PINAddressSearch2$pin2Box4"] = "",
                ["ctl00$PINAddressSearch2$pin2Box5"] = "",
                ["ctl00$HiddenField1"] = "",
                ["ctl00$ContentPlaceHolder1$PINAddressSearch$searchToValidate"] = "PIN",
                ["ctl00$ContentPlaceHolder1$PINAddressSearch$pinBox1"] = pinParts[0],
                ["ctl00$ContentPlaceHolder1$PINAddressSearch$pinBox2"] = pinParts[1],
                ["ctl00$ContentPlaceHolder1$PINAddressSearch$pinBox3"] = pinParts[2],
                ["ctl00$ContentPlaceHolder1$PINAddressSearch$pinBox4"] = pinParts[3],
                ["ctl00$ContentPlaceHolder1$PINAddressSearch$pinBox5"] = pinParts[4],
                ["ctl00$ContentPlaceHolder1$PINAddressSearch$btnSearch"] = "SEARCH",
                ["g-recaptcha-response"] = "",
                ["action"] = "validate_captcha"
            });
            
            client.DefaultRequestHeaders.Add("Referer", $"{TaxPortalBaseUrl}/default.aspx");
            
            _logger.LogInformation("Submitting PIN search form for {Pin}", pin);
            var postResponse = await client.PostAsync($"{TaxPortalBaseUrl}/pinresults.aspx", formContent);
            
            if (!postResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("Form submission failed with status {Status}", postResponse.StatusCode);
                return Error("Failed to search property information", "FETCH_ERROR");
            }
            
            var html = await postResponse.Content.ReadAsStringAsync();
            
            // Validate that we got property results, not just the search form
            var hasPropertyData = html.Contains("ContentPlaceHolder1_PropertyInfo") || 
                                  html.Contains("TAX BILLED AMOUNTS") ||
                                  html.Contains("Property Characteristics for PIN") ||
                                  html.Contains("lblResultTitle") ||
                                  html.Contains("2024 Assessed Value");
                                  
            if (!hasPropertyData)
            {
                _logger.LogWarning("Tax portal form submission did not return property data, trying Assessor site fallback");
                return await FetchFromAssessorFallback(pin, cacheKey);
            }
            
            // Parse and clean the HTML
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // Remove header, footer, navigation, scripts, styles
            RemoveElements(doc.DocumentNode, "//header | //footer | //nav | //script | //style | //noscript");
            
            // Remove the search form sections (the hidden search boxes at top)
            RemoveElements(doc.DocumentNode, "//div[contains(@class, 'searchcontent2')]");
            RemoveElements(doc.DocumentNode, "//div[@id='pinsearch2']");
            RemoveElements(doc.DocumentNode, "//div[@id='pinsearchaddress2']");
            RemoveElements(doc.DocumentNode, "//div[contains(@class, 'modal')]");
            RemoveElements(doc.DocumentNode, "//div[contains(@class, 'tophomelayout')]");
            RemoveElements(doc.DocumentNode, "//div[contains(@class, 'topmenu')]");
            
            // Find the main content area - the results section
            var resultsContent = doc.DocumentNode.SelectSingleNode("//div[@id='ContentPlaceHolder1_pnlResults']")
                ?? doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'resultspage')]")
                ?? doc.DocumentNode.SelectSingleNode("//div[@id='resultsection']")
                ?? doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'results')]")
                ?? doc.DocumentNode.SelectSingleNode("//form[@id='form1']");

            if (resultsContent == null)
            {
                _logger.LogWarning("Could not find results content in tax portal response");
                return Error("Property not found in tax portal", "NOT_FOUND");
            }
            
            // Remove hidden inputs and validation elements
            RemoveElements(resultsContent, ".//input[@type='hidden']");
            RemoveElements(resultsContent, ".//*[contains(@style, 'display:none')]");
            
            // Skip Street View image conversion here for performance
            // The PropertySummaryService extracts the raw URL and fetches it in parallel
            
            var cleanedHtml = resultsContent.OuterHtml;
            
            // Fix relative URLs to point to cookcountypropertyinfo.com
            // Skip URLs that already start with http, https, or data:
            cleanedHtml = Regex.Replace(cleanedHtml, @"(href|src)=""(?!http|data:)([^""]+)""", 
                "$1=\"https://www.cookcountypropertyinfo.com/$2\"", RegexOptions.IgnoreCase);
            
            // Fix double slashes in URLs
            cleanedHtml = cleanedHtml.Replace("cookcountypropertyinfo.com//", "cookcountypropertyinfo.com/");
            
            // Fix parent directory references (/../) in URLs
            cleanedHtml = cleanedHtml.Replace("cookcountypropertyinfo.com/../", "cookcountypropertyinfo.com/");

            // Cache the result
            _cache.Set(cacheKey, cleanedHtml, CacheDuration);

            return new ApiSuccessResponse<TaxPortalData>
            {
                Data = new TaxPortalData
                {
                    Html = cleanedHtml,
                    Pin = pin,
                    FetchedAt = DateTime.UtcNow.ToString("o")
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tax portal fetch error for PIN: {Pin}", pin);
            return Error("Failed to fetch tax portal data", "FETCH_ERROR");
        }
    }

    /// <summary>
    /// Fallback: Fetch property data from Cook County Assessor's Office
    /// Used when direct form submission to Tax Portal fails
    /// </summary>
    private async Task<object> FetchFromAssessorFallback(string pin, string cacheKey)
    {
        try
        {
            // Convert PIN format: 16-06-310-022-0000 -> 16063100220000
            var cleanPin = pin.Replace("-", "");
            
            var httpClient = _httpClientFactory.CreateClient("CookCounty");
            var assessorUrl = $"https://www.cookcountyassessor.com/pin/{cleanPin}";
            
            _logger.LogInformation("Fetching from Assessor fallback for PIN {Pin}", pin);
            var response = await httpClient.GetAsync(assessorUrl);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Assessor fallback returned status {Status}", response.StatusCode);
                return Error("Failed to fetch property information", "FETCH_ERROR");
            }
            
            var html = await response.Content.ReadAsStringAsync();
            
            // Check if the PIN is valid
            if (html.Contains("is not currently a valid PIN"))
            {
                _logger.LogWarning("Assessor reports invalid PIN: {Pin}", pin);
                return Error("Property not found - invalid PIN", "NOT_FOUND");
            }
            
            // Parse and clean the HTML
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            
            // Find the property detail content area - specific to Assessor site structure
            var mainContent = doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'property-detail')]")
                ?? doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'region-content')]")
                ?? doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'dialog-off-canvas-main-canvas')]");
            
            if (mainContent == null)
            {
                _logger.LogWarning("Could not find property-detail content in Assessor response");
                return Error("Failed to parse property information", "PARSE_ERROR");
            }
            
            // Remove navigation, header, footer, scripts
            RemoveElements(mainContent, ".//header | .//footer | .//nav | .//script | .//style | .//noscript | .//link");
            RemoveElements(mainContent, ".//aside");
            RemoveElements(mainContent, ".//*[contains(@class, 'sidebar')]");
            RemoveElements(mainContent, ".//*[contains(@class, 'navbar')]");
            RemoveElements(mainContent, ".//*[contains(@class, 'alert')]");
            RemoveElements(mainContent, ".//form");
            
            var cleanedHtml = mainContent.OuterHtml;
            
            // Fix relative URLs
            cleanedHtml = Regex.Replace(cleanedHtml, @"(href|src)=""(/[^""]+)""", 
                "$1=\"https://www.cookcountyassessor.com$2\"", RegexOptions.IgnoreCase);
            
            // Add a styled banner with source info and link to Tax Portal
            var taxPortalUrl = "https://www.cookcountypropertyinfo.com";
            cleanedHtml = $@"
<div style=""padding: 16px; margin-bottom: 20px; background: linear-gradient(135deg, #e8f4fd 0%, #f0f7ff 100%); border: 1px solid #0066cc; border-radius: 8px; font-family: system-ui, -apple-system, sans-serif;"">
    <div style=""display: flex; align-items: center; gap: 10px; margin-bottom: 8px;"">
        <svg width=""20"" height=""20"" viewBox=""0 0 24 24"" fill=""none"" stroke=""#0066cc"" stroke-width=""2""><path d=""M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z""/></svg>
        <strong style=""color: #0066cc; font-size: 15px;"">Data from Cook County Assessor's Office</strong>
    </div>
    <p style=""margin: 0 0 12px 0; color: #333; font-size: 14px; line-height: 1.5;"">
        This shows property details, assessed values, and characteristics. For <strong>tax bill history</strong> and <strong>payment information</strong>, visit the Tax Portal directly.
    </p>
    <a href=""{taxPortalUrl}"" target=""_blank"" rel=""noopener"" 
       style=""display: inline-block; padding: 8px 16px; background-color: #0066cc; color: white; text-decoration: none; border-radius: 4px; font-size: 14px; font-weight: 500;"">
        View Tax Bill History →
    </a>
</div>
{cleanedHtml}";
            
            // Cache the result
            _cache.Set(cacheKey, cleanedHtml, CacheDuration);
            
            return new ApiSuccessResponse<TaxPortalData>
            {
                Data = new TaxPortalData
                {
                    Html = cleanedHtml,
                    Pin = pin,
                    FetchedAt = DateTime.UtcNow.ToString("o")
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Assessor fallback fetch error for PIN: {Pin}", pin);
            return Error("Failed to fetch property information", "FETCH_ERROR");
        }
    }

    /// <summary>
    /// Tab 2: County Clerk - fetches from taxdelinquent.cookcountyclerkil.gov
    /// Shows sold and delinquent taxes
    /// </summary>
    public async Task<object> FetchCountyClerkAsync(string pin)
    {
        if (!ValidatePin(pin))
        {
            return Error("Invalid or missing PIN. Format: XX-XX-XXX-XXX-XXXX", "INVALID_PIN");
        }

        var cacheKey = $"countyclerk_{pin}";
        if (_cache.TryGetValue(cacheKey, out string? cachedHtml) && cachedHtml != null)
        {
            return new ApiSuccessResponse<TaxSearchData>
            {
                Data = new TaxSearchData
                {
                    Html = cachedHtml,
                    Pin = pin,
                    FetchedAt = DateTime.UtcNow.ToString("o")
                }
            };
        }

        try
        {
            var baseUrl = "https://taxdelinquent.cookcountyclerkil.gov";
            var httpClient = _httpClientFactory.CreateClient("CookCounty");

            // First request to get the verification token
            var initialResponse = await httpClient.GetAsync($"{baseUrl}/");
            var initialHtml = await initialResponse.Content.ReadAsStringAsync();

            // Extract the token
            var tokenMatch = Regex.Match(initialHtml, @"name=""__RequestVerificationToken"" type=""hidden"" value=""([^""]+)""");
            if (!tokenMatch.Success)
            {
                return Error("Failed to initialize search", "PARSE_ERROR");
            }
            var token = tokenMatch.Groups[1].Value;

            // Get cookies from response
            IEnumerable<string>? cookieValues = null;
            initialResponse.Headers.TryGetValues("Set-Cookie", out cookieValues);
            var cookies = cookieValues?.FirstOrDefault() ?? "";

            // Prepare POST data
            var formData = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("__RequestVerificationToken", token),
                new KeyValuePair<string, string>("Pin", pin)
            });

            var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/");
            request.Content = formData;
            if (!string.IsNullOrEmpty(cookies))
            {
                request.Headers.Add("Cookie", cookies);
            }

            var response = await httpClient.SendAsync(request);
            var html = await response.Content.ReadAsStringAsync();

            // Parse and clean the HTML
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // Find the results container
            var resultsContent = doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'container-fluid')]")
                ?? doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'results')]")
                ?? doc.DocumentNode.SelectSingleNode("//body");

            if (resultsContent == null)
            {
                return Error("Failed to parse tax search results", "PARSE_ERROR");
            }

            // Remove header, footer, nav, scripts
            RemoveElements(resultsContent, ".//header | .//footer | .//nav | .//script | .//style | .//noscript");
            
            // Remove the search form
            RemoveElements(resultsContent, ".//form[contains(@action, '/')]");

            var cleanedHtml = resultsContent.OuterHtml;

            // Fix relative URLs
            cleanedHtml = Regex.Replace(cleanedHtml, @"(href|src)=""(/[^""]+)""", $"$1=\"{baseUrl}$2\"", RegexOptions.IgnoreCase);

            // Add styling fix for padding
            cleanedHtml = $"<div style=\"padding-top: 0 !important;\">{cleanedHtml}</div>";

            // Cache the result
            _cache.Set(cacheKey, cleanedHtml, CacheDuration);

            return new ApiSuccessResponse<TaxSearchData>
            {
                Data = new TaxSearchData
                {
                    Html = cleanedHtml,
                    Pin = pin,
                    FetchedAt = DateTime.UtcNow.ToString("o")
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "County clerk fetch error for PIN: {Pin}", pin);
            return Error("Failed to fetch county clerk data", "FETCH_ERROR");
        }
    }

    /// <summary>
    /// Tab 3: County Treasurer - fetches from cookcountytreasurer.com
    /// Shows payment status and tax overview
    /// Uses ASP.NET WebForms form submission with empty GoogleCaptchaToken
    /// </summary>
    public async Task<object> FetchTreasurerAsync(string pin)
    {
        if (!ValidatePin(pin))
        {
            return Error("Invalid or missing PIN. Format: XX-XX-XXX-XXX-XXXX", "INVALID_PIN");
        }

        var cacheKey = $"treasurer_{pin}";
        if (_cache.TryGetValue(cacheKey, out string? cachedHtml) && cachedHtml != null)
        {
            return new ApiSuccessResponse<TreasurerData>
            {
                Data = new TreasurerData
                {
                    Html = cachedHtml,
                    Pin = pin,
                    FetchedAt = DateTime.UtcNow.ToString("o")
                }
            };
        }

        try
        {
            var baseUrl = "https://www.cookcountytreasurer.com";
            
            // Use a handler with cookies to maintain session
            var handler = new HttpClientHandler
            {
                CookieContainer = new CookieContainer(),
                UseCookies = true,
                AllowAutoRedirect = true
            };
            
            using var client = new HttpClient(handler);
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
            client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.5");
            
            // Step 1: GET the search page to extract ASP.NET state tokens
            _logger.LogInformation("Fetching Treasurer search page to get form tokens for PIN {Pin}", pin);
            var getResponse = await client.GetAsync($"{baseUrl}/setsearchparameters.aspx");
            
            if (!getResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to get Treasurer search page: {Status}", getResponse.StatusCode);
                return Error("Failed to access Treasurer portal", "FETCH_ERROR");
            }
            
            var initialHtml = await getResponse.Content.ReadAsStringAsync();
            
            // Extract ASP.NET state tokens
            var viewState = ExtractFormValue(initialHtml, "__VIEWSTATE");
            var viewStateGenerator = ExtractFormValue(initialHtml, "__VIEWSTATEGENERATOR");
            var eventValidation = ExtractFormValue(initialHtml, "__EVENTVALIDATION");
            
            if (string.IsNullOrEmpty(viewState) || string.IsNullOrEmpty(eventValidation))
            {
                _logger.LogWarning("Could not extract form tokens from Treasurer search page");
                return Error("Failed to initialize Treasurer search", "FETCH_ERROR");
            }
            
            // Split PIN into parts: 16-06-310-022-0000 -> [16, 06, 310, 022, 0000]
            var pinParts = pin.Split('-');
            
            // Step 2: POST the form with PIN search
            // Key insight: The CAPTCHA is only enforced client-side, server accepts empty token
            // Form fields are nested under ASPxPanel1$SearchByPIN1
            var formContent = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__EVENTTARGET"] = "",
                ["__EVENTARGUMENT"] = "",
                ["__VIEWSTATE"] = viewState,
                ["__VIEWSTATEGENERATOR"] = viewStateGenerator,
                ["__VIEWSTATEENCRYPTED"] = "",
                ["__EVENTVALIDATION"] = eventValidation,
                ["ctl00$ContentPlaceHolder1$ASPxPanel1$SearchByPIN1$txtPIN1"] = pinParts[0],
                ["ctl00$ContentPlaceHolder1$ASPxPanel1$SearchByPIN1$txtPIN2"] = pinParts[1],
                ["ctl00$ContentPlaceHolder1$ASPxPanel1$SearchByPIN1$txtPIN3"] = pinParts[2],
                ["ctl00$ContentPlaceHolder1$ASPxPanel1$SearchByPIN1$txtPIN4"] = pinParts[3],
                ["ctl00$ContentPlaceHolder1$ASPxPanel1$SearchByPIN1$txtPIN5"] = pinParts[4],
                ["ctl00$ContentPlaceHolder1$ASPxPanel1$SearchByPIN1$cmdContinue"] = "Continue",
                ["ctl00$ContentPlaceHolder1$PIN"] = "",
                ["ctl00$ContentPlaceHolder1$SearchType"] = "",
                ["GoogleCaptchaToken"] = ""
            });
            
            client.DefaultRequestHeaders.Add("Referer", $"{baseUrl}/setsearchparameters.aspx");
            
            _logger.LogInformation("Submitting Treasurer PIN search form for {Pin}", pin);
            var postResponse = await client.PostAsync($"{baseUrl}/setsearchparameters.aspx", formContent);
            
            if (!postResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("Treasurer form submission failed with status {Status}", postResponse.StatusCode);
                return Error("Failed to search Treasurer information", "FETCH_ERROR");
            }
            
            var html = await postResponse.Content.ReadAsStringAsync();
            
            // Check if we got redirected to results or got an error
            // The Treasurer site may redirect to a results page
            var finalUrl = postResponse.RequestMessage?.RequestUri?.ToString() ?? "";
            _logger.LogInformation("Treasurer response URL: {Url}", finalUrl);
            
            // Parse and clean the HTML
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // Remove header, footer, navigation, scripts, styles
            RemoveElements(doc.DocumentNode, "//header | //footer | //nav | //script | //style | //noscript | //link");
            RemoveElements(doc.DocumentNode, "//div[@id='PageHeader']");
            RemoveElements(doc.DocumentNode, "//div[@id='PageFooter']");
            RemoveElements(doc.DocumentNode, "//div[contains(@class, 'bannersection')]");
            RemoveElements(doc.DocumentNode, "//div[contains(@class, 'horizontalnavigationsection')]");
            RemoveElements(doc.DocumentNode, "//div[contains(@class, 'submenussection')]");
            RemoveElements(doc.DocumentNode, "//div[contains(@class, 'closemenu')]");
            RemoveElements(doc.DocumentNode, "//div[contains(@class, 'navbar')]");
            RemoveElements(doc.DocumentNode, "//div[contains(@id, 'menu')]");
            RemoveElements(doc.DocumentNode, "//input[@type='hidden']");
            RemoveElements(doc.DocumentNode, "//div[contains(@id, 'NoPrintRegion')]");
            
            // Find the main content area - look for the PageContent or results panel
            var mainContent = doc.DocumentNode.SelectSingleNode("//div[@id='PageContent']")
                ?? doc.DocumentNode.SelectSingleNode("//div[contains(@id, 'OverviewDataResultsSummary')]")
                ?? doc.DocumentNode.SelectSingleNode("//div[contains(@id, 'ContentPlaceHolder1')]")
                ?? doc.DocumentNode.SelectSingleNode("//form[@id='form1']//div[contains(@class, 'pagesection')]")
                ?? doc.DocumentNode.SelectSingleNode("//body");

            if (mainContent == null)
            {
                _logger.LogWarning("Could not find content in Treasurer response");
                return Error("Failed to parse Treasurer data", "PARSE_ERROR");
            }
            
            // Additional cleanup on the content
            RemoveElements(mainContent, ".//div[contains(@class, 'submenussection')]");
            RemoveElements(mainContent, ".//div[contains(@style, 'display: none')]");
            RemoveElements(mainContent, ".//div[contains(@style, 'display:none')]");
            
            var cleanedHtml = mainContent.OuterHtml;
            
            // Fix relative URLs to point to cookcountytreasurer.com
            cleanedHtml = Regex.Replace(cleanedHtml, @"(href|src)=""(?!http)([^""]+)""", 
                $"$1=\"{baseUrl}/$2\"", RegexOptions.IgnoreCase);
            
            // Fix double slashes in URLs
            cleanedHtml = cleanedHtml.Replace("cookcountytreasurer.com//", "cookcountytreasurer.com/");

            // Add a styled header and CSS fixes for layout alignment
            cleanedHtml = $@"
<style>
    .treasurer-content .propertyvaluewrapper,
    .treasurer-content .debtpercentagewrapper,
    .treasurer-content .totaldebtdollarswrapper {{
        display: flex !important;
        flex-wrap: wrap !important;
        align-items: baseline !important;
        gap: 8px !important;
        margin-bottom: 12px !important;
    }}
    .treasurer-content .propertyvaluewrapper > div,
    .treasurer-content .debtpercentagewrapper > div,
    .treasurer-content .totaldebtdollarswrapper > div {{
        float: none !important;
        display: inline !important;
    }}
    .treasurer-content .blacktextbold {{
        font-weight: 600 !important;
        margin-right: 8px !important;
    }}
    .treasurer-content .redtextbold {{
        font-weight: 700 !important;
        color: #dc2626 !important;
    }}
    .treasurer-content [style*='float: left'],
    .treasurer-content [style*='float:left'] {{
        float: none !important;
        display: inline-block !important;
        vertical-align: baseline !important;
    }}
    .treasurer-content .taxingdistrictsdesktop > div {{
        float: none !important;
        display: inline-block !important;
        padding: 4px 8px !important;
    }}
    .treasurer-content table {{
        width: 100% !important;
        border-collapse: collapse !important;
    }}
    .treasurer-content td {{
        padding: 6px 8px !important;
        vertical-align: top !important;
    }}
    .treasurer-content .clear {{
        clear: both !important;
        height: 0 !important;
    }}
</style>
<div class=""treasurer-content"" style=""font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;"">
    <div style=""background: linear-gradient(135deg, #1e40af 0%, #3b82f6 100%); color: white; padding: 16px 20px; border-radius: 8px; margin-bottom: 16px;"">
        <h3 style=""margin: 0; font-size: 16px; font-weight: 600;"">Cook County Treasurer - Payment Status</h3>
        <p style=""margin: 4px 0 0 0; opacity: 0.9; font-size: 13px;"">PIN: {pin}</p>
    </div>
    {cleanedHtml}
</div>";

            // Cache the result
            _cache.Set(cacheKey, cleanedHtml, CacheDuration);

            return new ApiSuccessResponse<TreasurerData>
            {
                Data = new TreasurerData
                {
                    Html = cleanedHtml,
                    Pin = pin,
                    FetchedAt = DateTime.UtcNow.ToString("o")
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Treasurer fetch error for PIN: {Pin}", pin);
            return Error("Failed to fetch Treasurer data", "FETCH_ERROR");
        }
    }

    /// <summary>
    /// Tab 4: Recorder - fetches from crs.cookcountyclerkil.gov
    /// Shows recorded documents for the property
    /// Uses a multi-step approach:
    /// 1. GET the search page to extract anti-forgery token and cookies
    /// 2. POST AddressSearch form to find property records
    /// 3. If address results found, follow the ResultByPin link for document details
    /// 4. Combine both results for comprehensive display
    /// </summary>
    public async Task<object> FetchRecorderAsync(string pin)
    {
        if (!ValidatePin(pin))
        {
            return Error("Invalid or missing PIN. Format: XX-XX-XXX-XXX-XXXX", "INVALID_PIN");
        }

        var cacheKey = $"recorder_{pin}";
        if (_cache.TryGetValue(cacheKey, out string? cachedHtml) && cachedHtml != null)
        {
            return new ApiSuccessResponse<RecorderData>
            {
                Data = new RecorderData
                {
                    Html = cachedHtml,
                    Pin = pin,
                    FetchedAt = DateTime.UtcNow.ToString("o")
                }
            };
        }

        try
        {
            var baseUrl = "https://crs.cookcountyclerkil.gov";
            var cleanPin = pin.Replace("-", "");

            var handler = new HttpClientHandler
            {
                CookieContainer = new System.Net.CookieContainer(),
                UseCookies = true,
                AllowAutoRedirect = true
            };
            using var client = new HttpClient(handler);
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            client.Timeout = TimeSpan.FromSeconds(30);

            // Step 1: GET the search page to extract anti-forgery token
            var searchPageResponse = await client.GetAsync($"{baseUrl}/Search");
            if (!searchPageResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("Recorder search page returned {StatusCode}", searchPageResponse.StatusCode);
                return await FetchRecorderDirectAsync(pin, baseUrl);
            }
            var searchPageHtml = await searchPageResponse.Content.ReadAsStringAsync();

            var tokenMatch = Regex.Match(searchPageHtml, @"name=""__RequestVerificationToken"".*?value=""([^""]+)""");
            if (!tokenMatch.Success)
            {
                _logger.LogWarning("Failed to extract anti-forgery token from recorder search page");
                return await FetchRecorderDirectAsync(pin, baseUrl);
            }
            var token = tokenMatch.Groups[1].Value;

            // Step 2: POST AddressSearch form to find property by PIN
            var formContent = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("__RequestVerificationToken", token),
                new KeyValuePair<string, string>("inputField", pin),
                new KeyValuePair<string, string>("submitButton", "AddressSearch")
            });

            var searchRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/Search?strAction=Search")
            {
                Content = formContent
            };
            searchRequest.Headers.Referrer = new Uri($"{baseUrl}/Search");

            var searchResponse = await client.SendAsync(searchRequest);
            if (!searchResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("Recorder address search returned {StatusCode}", searchResponse.StatusCode);
                return await FetchRecorderDirectAsync(pin, baseUrl);
            }
            var addressHtml = await searchResponse.Content.ReadAsStringAsync();

            var hasAddressResults = !string.IsNullOrEmpty(addressHtml) &&
                addressHtml.Contains("Total Records", StringComparison.OrdinalIgnoreCase) &&
                addressHtml.Contains("<table", StringComparison.OrdinalIgnoreCase);

            // Step 3: If AddressSearch found results, extract and follow the ResultByPin link
            string? resultByPinHtml = null;
            var hasDocuments = false;

            if (hasAddressResults)
            {
                var resultByPinLinkMatch = Regex.Match(addressHtml, @"href=""(/Search/ResultByPin\?id1=[^""]+)""");
                var resultByPinUrl = resultByPinLinkMatch.Success
                    ? $"{baseUrl}{resultByPinLinkMatch.Groups[1].Value}"
                    : $"{baseUrl}/Search/ResultByPin?id1={cleanPin}";

                var resultByPinResponse = await client.GetAsync(resultByPinUrl);
                if (resultByPinResponse.IsSuccessStatusCode)
                {
                    resultByPinHtml = await resultByPinResponse.Content.ReadAsStringAsync();
                    hasDocuments = !string.IsNullOrEmpty(resultByPinHtml) &&
                        !resultByPinHtml.Contains("No Document(s) found", StringComparison.OrdinalIgnoreCase) &&
                        !resultByPinHtml.Contains("No documents found", StringComparison.OrdinalIgnoreCase);
                }
            }
            else
            {
                var resultByPinResponse = await client.GetAsync($"{baseUrl}/Search/ResultByPin?id1={cleanPin}");
                if (resultByPinResponse.IsSuccessStatusCode)
                {
                    resultByPinHtml = await resultByPinResponse.Content.ReadAsStringAsync();
                    hasDocuments = !string.IsNullOrEmpty(resultByPinHtml) &&
                        !resultByPinHtml.Contains("No Document(s) found", StringComparison.OrdinalIgnoreCase) &&
                        !resultByPinHtml.Contains("No documents found", StringComparison.OrdinalIgnoreCase);
                }
            }

            if (!hasDocuments && !hasAddressResults)
            {
                return Error("No recorded documents found for this PIN", "NOT_FOUND");
            }

            string? finalHtml;

            if (hasDocuments && resultByPinHtml != null)
            {
                finalHtml = CleanRecorderHtml(resultByPinHtml, baseUrl);
            }
            else
            {
                finalHtml = CleanRecorderHtml(addressHtml, baseUrl);
            }

            if (string.IsNullOrEmpty(finalHtml))
            {
                return Error("Failed to parse recorder results", "PARSE_ERROR");
            }

            _cache.Set(cacheKey, finalHtml, CacheDuration);

            return new ApiSuccessResponse<RecorderData>
            {
                Data = new RecorderData
                {
                    Html = finalHtml,
                    Pin = pin,
                    FetchedAt = DateTime.UtcNow.ToString("o")
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Recorder fetch error for PIN: {Pin}", pin);
            return Error("Failed to fetch recorder data", "FETCH_ERROR");
        }
    }

    public async Task<Dictionary<string, string>> FetchRecorderConsiderationsAsync(string pin, List<(string docNumber, string viewUrl)> documents)
    {
        var result = new Dictionary<string, string>();
        if (documents == null || documents.Count == 0)
            return result;

        var baseUrl = "https://crs.cookcountyclerkil.gov";
        var cleanPin = pin.Replace("-", "");

        var handler = new HttpClientHandler
        {
            CookieContainer = new System.Net.CookieContainer(),
            UseCookies = true,
            AllowAutoRedirect = true
        };
        using var client = new HttpClient(handler);
        client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        client.Timeout = TimeSpan.FromSeconds(15);

        try
        {
            var searchPageResponse = await client.GetAsync($"{baseUrl}/Search");
            if (!searchPageResponse.IsSuccessStatusCode)
                return result;
            var searchPageHtml = await searchPageResponse.Content.ReadAsStringAsync();

            var tokenMatch = Regex.Match(searchPageHtml, @"name=""__RequestVerificationToken"".*?value=""([^""]+)""");
            if (!tokenMatch.Success)
                return result;

            var formContent = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("__RequestVerificationToken", tokenMatch.Groups[1].Value),
                new KeyValuePair<string, string>("inputField", pin),
                new KeyValuePair<string, string>("submitButton", "AddressSearch")
            });
            var searchRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/Search?strAction=Search")
            {
                Content = formContent
            };
            searchRequest.Headers.Referrer = new Uri($"{baseUrl}/Search");
            var searchResponse = await client.SendAsync(searchRequest);
            if (!searchResponse.IsSuccessStatusCode)
                return result;

            var tasks = documents.Select(async doc =>
            {
                try
                {
                    var url = System.Net.WebUtility.HtmlDecode(doc.viewUrl);
                    if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        url = $"{baseUrl}{url}";

                    var response = await client.GetAsync(url);
                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogWarning("Consideration detail page returned {StatusCode} for doc {Doc}", response.StatusCode, doc.docNumber);
                        return;
                    }
                    var html = await response.Content.ReadAsStringAsync();
                    var detailDoc = new HtmlAgilityPack.HtmlDocument();
                    detailDoc.LoadHtml(html);

                    var considerationNode = detailDoc.DocumentNode.SelectSingleNode(
                        "//th[.//label[contains(text(),'Consideration Amount')]]/following-sibling::td")
                        ?? detailDoc.DocumentNode.SelectSingleNode(
                        "//td[contains(text(),'Consideration Amount')]/following-sibling::td");
                    if (considerationNode != null)
                    {
                        var val = considerationNode.InnerText.Trim();
                        if (!string.IsNullOrEmpty(val))
                        {
                            lock (result)
                            {
                                result[doc.docNumber] = val;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to fetch consideration for doc {DocNumber}", doc.docNumber);
                }
            });

            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to establish recorder session for considerations");
        }

        return result;
    }

    private async Task<object> FetchRecorderDirectAsync(string pin, string baseUrl)
    {
        var cleanPin = pin.Replace("-", "");
        var httpClient = _httpClientFactory.CreateClient("CookCounty");
        var searchUrl = $"{baseUrl}/Search/ResultByPin?id1={cleanPin}";
        var response = await httpClient.GetAsync(searchUrl);
        var html = await response.Content.ReadAsStringAsync();

        if (string.IsNullOrEmpty(html))
        {
            return Error("Failed to fetch recorder data", "FETCH_ERROR");
        }

        if (html.Contains("No Document(s) found", StringComparison.OrdinalIgnoreCase) ||
            html.Contains("No documents found", StringComparison.OrdinalIgnoreCase))
        {
            return Error("No recorded documents found for this PIN", "NOT_FOUND");
        }

        var cleanedHtml = CleanRecorderHtml(html, baseUrl);
        if (string.IsNullOrEmpty(cleanedHtml))
        {
            return Error("Failed to parse recorder results", "PARSE_ERROR");
        }

        var cacheKey = $"recorder_{pin}";
        _cache.Set(cacheKey, cleanedHtml, CacheDuration);

        return new ApiSuccessResponse<RecorderData>
        {
            Data = new RecorderData
            {
                Html = cleanedHtml,
                Pin = pin,
                FetchedAt = DateTime.UtcNow.ToString("o")
            }
        };
    }

    private string? CleanRecorderHtml(string html, string baseUrl)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var resultsContent = doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'container-box')]")
            ?? doc.DocumentNode.SelectSingleNode("//div[@id='result']")?.ParentNode
            ?? doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'table-responsive')]")
            ?? doc.DocumentNode.SelectSingleNode("//table[contains(@class, 'table')]")?.ParentNode?.ParentNode;

        if (resultsContent == null)
        {
            var mainContainers = doc.DocumentNode.SelectNodes("//div[@class='container']");
            if (mainContainers != null && mainContainers.Count > 1)
            {
                resultsContent = mainContainers[1];
            }
        }

        if (resultsContent == null)
        {
            return null;
        }

        RemoveElements(resultsContent, ".//header | .//footer | .//script | .//style | .//noscript");
        RemoveElements(resultsContent, ".//form[contains(@action, 'Login')]");
        RemoveElements(resultsContent, ".//nav");
        RemoveElements(resultsContent, ".//a[contains(@href, 'javascript:')]");

        var cleanedHtml = resultsContent.OuterHtml;

        cleanedHtml = Regex.Replace(cleanedHtml, @"(href|src)=""(?!(?:https?://|data:))(/[^""]+)""", $"$1=\"{baseUrl}$2\"", RegexOptions.IgnoreCase);

        cleanedHtml = Regex.Replace(cleanedHtml, @"<a ([^>]*href=""[^""]*(?:Document/Detail|ResultByPin)[^""]*"")", "<a target=\"_blank\" rel=\"noopener noreferrer\" $1", RegexOptions.IgnoreCase);

        var styleOverrides = @"
<style>
    .container-box { padding: 5px 0 !important; margin: 0 !important; }
    .container-box .row { margin-left: 0 !important; margin-right: 0 !important; }
    .container-box .col-md-1, .container-box .col-md-4 { padding-left: 2px !important; padding-right: 2px !important; }
    .container-box table, .table-responsive table { font-size: 13px !important; width: 100% !important; }
    .container-box th, .container-box td, .table-responsive th, .table-responsive td { padding: 6px 8px !important; }
    th:last-child, td:last-child { white-space: normal !important; word-wrap: break-word !important; }
    .container-box legend { font-size: 14px !important; margin-bottom: 6px !important; }
    .container-box fieldset { margin: 0 !important; padding: 0 !important; }
    .container-box label { margin-bottom: 0 !important; font-size: 12px !important; }
    .table-responsive { margin: 0 !important; overflow-x: auto !important; -webkit-overflow-scrolling: touch; }
    a[target='_blank'] { color: #2563eb !important; text-decoration: underline !important; }
    a[target='_blank']:hover { color: #1d4ed8 !important; }
</style>";
        cleanedHtml = styleOverrides + cleanedHtml;

        return cleanedHtml;
    }

    /// <summary>
    /// Tab 5: Assessor - fetches from cookcountyassessoril.gov
    /// Shows property details, assessed values, and characteristics
    /// Simple GET request with PIN in URL path
    /// </summary>
    public async Task<object> FetchAssessorAsync(string pin)
    {
        if (!ValidatePin(pin))
        {
            return Error("Invalid or missing PIN. Format: XX-XX-XXX-XXX-XXXX", "INVALID_PIN");
        }

        var cacheKey = $"assessor_{pin}";
        if (_cache.TryGetValue(cacheKey, out string? cachedHtml) && cachedHtml != null)
        {
            return new ApiSuccessResponse<AssessorData>
            {
                Data = new AssessorData
                {
                    Html = cachedHtml,
                    Pin = pin,
                    FetchedAt = DateTime.UtcNow.ToString("o")
                }
            };
        }

        try
        {
            var baseUrl = "https://www.cookcountyassessoril.gov";
            // Convert PIN format: 16-06-310-022-0000 -> 16063100220000
            var cleanPin = pin.Replace("-", "");
            
            var httpClient = _httpClientFactory.CreateClient("CookCounty");
            var assessorUrl = $"{baseUrl}/pin/{cleanPin}";
            
            _logger.LogInformation("Fetching from Assessor for PIN {Pin}", pin);
            var response = await httpClient.GetAsync(assessorUrl);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Assessor returned status {Status}", response.StatusCode);
                return Error("Failed to fetch property information from Assessor", "FETCH_ERROR");
            }
            
            var html = await response.Content.ReadAsStringAsync();
            
            // Check if the PIN is valid
            if (html.Contains("is not currently a valid PIN") || html.Contains("Page not found"))
            {
                _logger.LogWarning("Assessor reports invalid PIN: {Pin}", pin);
                return Error("Property not found - invalid PIN", "NOT_FOUND");
            }
            
            // Parse and clean the HTML
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            
            // Find the property detail content area
            var mainContent = doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'property-detail')]")
                ?? doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'region-content')]");
            
            if (mainContent == null)
            {
                _logger.LogWarning("Could not find property-detail content in Assessor response");
                return Error("Failed to parse property information", "PARSE_ERROR");
            }
            
            // Remove navigation, header, footer, scripts, alerts
            RemoveElements(mainContent, ".//header | .//footer | .//nav | .//script | .//style | .//noscript | .//link");
            RemoveElements(mainContent, ".//*[contains(@class, 'assessor-alert')]");
            RemoveElements(mainContent, ".//*[contains(@class, 'navbar')]");
            RemoveElements(mainContent, ".//*[contains(@id, 'propertyImageModal')]");
            RemoveElements(mainContent, ".//form");
            
            var cleanedHtml = mainContent.OuterHtml;
            
            // Fix relative URLs
            cleanedHtml = Regex.Replace(cleanedHtml, @"(href|src)=""(/[^""]+)""", 
                $"$1=\"{baseUrl}$2\"", RegexOptions.IgnoreCase);
            
            // Make links open in new tab
            cleanedHtml = Regex.Replace(cleanedHtml, @"<a ([^>]*href=""http[^""]*"")", 
                "<a target=\"_blank\" rel=\"noopener noreferrer\" $1", RegexOptions.IgnoreCase);
            
            // Add header with source info
            var styledHeader = $@"
<div style=""font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;"">
    <div style=""background: linear-gradient(135deg, #059669 0%, #10b981 100%); color: white; padding: 16px 20px; border-radius: 8px; margin-bottom: 16px;"">
        <h3 style=""margin: 0; font-size: 16px; font-weight: 600;"">Cook County Assessor's Office</h3>
        <p style=""margin: 4px 0 0 0; opacity: 0.9; font-size: 13px;"">PIN: {pin}</p>
    </div>
    {cleanedHtml}
</div>";
            
            // Add CSS overrides for better display
            var styleOverrides = @"
<style>
    .property-detail { font-family: system-ui, -apple-system, sans-serif !important; }
    .property-detail h1 { font-size: 1.5rem !important; margin-bottom: 1rem !important; }
    .property-detail h2 { font-size: 1.25rem !important; margin: 1.5rem 0 0.75rem 0 !important; color: #374151 !important; }
    .property-detail .property-details-info { background: #f9fafb !important; padding: 1rem !important; border-radius: 8px !important; margin-bottom: 1rem !important; }
    .property-detail .detail-row--label { font-weight: 500 !important; color: #6b7280 !important; display: block !important; font-size: 0.875rem !important; }
    .property-detail .detail-row--detail { font-weight: 600 !important; color: #111827 !important; display: block !important; margin-bottom: 0.75rem !important; }
    .property-detail .img-container img { max-width: 100% !important; height: auto !important; border-radius: 8px !important; }
    .property-detail .slider-container { margin-bottom: 1rem !important; }
    .property-detail .tab { display: none !important; }
    .property-detail .see-all-photos { display: none !important; }
    .property-detail .banner-tab { margin-bottom: 1rem !important; }
    .property-detail .small { font-size: 0.75rem !important; color: #6b7280 !important; }
</style>";
            
            cleanedHtml = styleOverrides + styledHeader;

            // Cache the result
            _cache.Set(cacheKey, cleanedHtml, CacheDuration);

            return new ApiSuccessResponse<AssessorData>
            {
                Data = new AssessorData
                {
                    Html = cleanedHtml,
                    Pin = pin,
                    FetchedAt = DateTime.UtcNow.ToString("o")
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Assessor fetch error for PIN: {Pin}", pin);
            return Error("Failed to fetch assessor data", "FETCH_ERROR");
        }
    }

    public void ClearCache(string? pin = null)
    {
        if (!string.IsNullOrEmpty(pin))
        {
            _cache.Remove($"taxportal_{pin}");
            _cache.Remove($"countyclerk_{pin}");
            _cache.Remove($"treasurer_{pin}");
            _cache.Remove($"recorder_{pin}");
            _cache.Remove($"assessor_{pin}");
        }
        else
        {
            _logger.LogInformation("Cache clear requested for all entries");
        }
    }

    private static string? ExtractFormValue(string html, string fieldName)
    {
        var pattern = $@"id=""{fieldName}""[^>]*value=""([^""]*)""";
        var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return match.Groups[1].Value;
        }
        
        // Try alternative pattern
        pattern = $@"name=""{fieldName}""[^>]*value=""([^""]*)""";
        match = Regex.Match(html, pattern, RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    private async Task ConvertStreetViewImagesToBase64Async(HtmlNode parent, HttpClient httpClient)
    {
        var streetViewImages = parent.SelectNodes(".//img[contains(@src, 'maps.googleapis.com')]");
        if (streetViewImages == null) return;
        
        foreach (var img in streetViewImages.ToList())
        {
            var src = img.GetAttributeValue("src", "");
            if (string.IsNullOrEmpty(src)) continue;
            
            // Decode HTML entities in the URL
            src = System.Net.WebUtility.HtmlDecode(src);
            
            try
            {
                _logger.LogInformation("Fetching Street View image: {Url}", src.Substring(0, Math.Min(100, src.Length)));
                
                var imageBytes = await httpClient.GetByteArrayAsync(src);
                var base64 = Convert.ToBase64String(imageBytes);
                var dataUrl = $"data:image/jpeg;base64,{base64}";
                
                img.SetAttributeValue("src", dataUrl);
                _logger.LogInformation("Successfully converted Street View image to base64");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch Street View image, removing element");
                img.Remove();
            }
        }
    }

    private static void RemoveElements(HtmlNode parent, string xpath)
    {
        var elements = parent.SelectNodes(xpath);
        if (elements != null)
        {
            foreach (var element in elements.ToList())
            {
                element.Remove();
            }
        }
    }
}
