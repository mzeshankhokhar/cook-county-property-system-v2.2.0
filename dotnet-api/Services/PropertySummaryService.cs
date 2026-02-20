using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Caching.Memory;
using CookCountyApi.Models;

namespace CookCountyApi.Services;

public interface IPropertySummaryService
{
    Task<object> GetTaxPortalDataAsync(string pin);
    Task<object> GetClerkDataAsync(string pin);
    Task<object> GetRecorderDataAsync(string pin);
    Task<object> GetCookViewerDataAsync(string pin);
    Task<object> GetGoogleMapsDataAsync(double lat, double lon);
}

public class PropertySummaryService : IPropertySummaryService
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<PropertySummaryService> _logger;
    private readonly ICookCountyProxyService _proxyService;
    private readonly IHttpClientFactory _httpClientFactory;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);
    private static readonly Regex PinRegex = new(@"^\d{2}-\d{2}-\d{3}-\d{3}-\d{4}$", RegexOptions.Compiled);

    public PropertySummaryService(IMemoryCache cache, ILogger<PropertySummaryService> logger, ICookCountyProxyService proxyService, IHttpClientFactory httpClientFactory)
    {
        _cache = cache;
        _logger = logger;
        _proxyService = proxyService;
        _httpClientFactory = httpClientFactory;
    }

    private static bool ValidatePin(string pin) => !string.IsNullOrEmpty(pin) && PinRegex.IsMatch(pin);
    private static ApiErrorResponse Error(string message, string code) => new() { Error = message, Code = code };

    public async Task<object> GetTaxPortalDataAsync(string pin)
    {
        if (!ValidatePin(pin))
            return Error("Invalid or missing PIN. Format: XX-XX-XXX-XXX-XXXX", "INVALID_PIN");

        var cacheKey = $"taxportal_{pin}";
        if (_cache.TryGetValue(cacheKey, out TaxPortalStructuredData? cached) && cached != null)
            return new ApiSuccessResponse<TaxPortalStructuredData> { Data = cached };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var data = new TaxPortalStructuredData { Pin = pin };

        try
        {
            var result = await _proxyService.FetchTaxPortalAsync(pin);
            var tempSummary = new PropertySummaryData { Pin = pin, Errors = new Dictionary<string, string>() };
            var svUrl = ParseTaxPortalResult(result, tempSummary);

            data.PropertyInfo = tempSummary.PropertyInfo;
            data.Characteristics = tempSummary.Characteristics;
            data.TaxBills = tempSummary.TaxBills;
            data.TaxSaleDelinquencies = tempSummary.TaxSaleDelinquencies;
            data.PropertyImageBase64 = tempSummary.PropertyImageBase64;

            if (svUrl != null && data.PropertyImageBase64 == null)
            {
                var svSw = System.Diagnostics.Stopwatch.StartNew();
                var image = await FetchStreetViewImageAsync(svUrl);
                svSw.Stop();
                _logger.LogInformation("PERF: StreetView fetch completed in {Elapsed}ms", svSw.ElapsedMilliseconds);
                if (image != null)
                    data.PropertyImageBase64 = image;
            }

            if (tempSummary.Errors.ContainsKey("taxPortal"))
                data.Error = tempSummary.Errors["taxPortal"];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch tax portal data for {Pin}", pin);
            data.Error = $"Fetch failed: {ex.Message}";
        }

        sw.Stop();
        _logger.LogInformation("PERF: GetTaxPortalDataAsync for {Pin} completed in {Elapsed}ms", pin, sw.ElapsedMilliseconds);

        _cache.Set(cacheKey, data, CacheDuration);
        return new ApiSuccessResponse<TaxPortalStructuredData> { Data = data };
    }

    public async Task<object> GetClerkDataAsync(string pin)
    {
        if (!ValidatePin(pin))
            return Error("Invalid or missing PIN. Format: XX-XX-XXX-XXX-XXXX", "INVALID_PIN");

        var cacheKey = $"clerk_{pin}";
        if (_cache.TryGetValue(cacheKey, out ClerkStructuredData? cached) && cached != null)
            return new ApiSuccessResponse<ClerkStructuredData> { Data = cached };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var data = new ClerkStructuredData { Pin = pin };

        try
        {
            var result = await _proxyService.FetchCountyClerkAsync(pin);
            var tempSummary = new PropertySummaryData { Pin = pin, Errors = new Dictionary<string, string>() };
            ParseClerkResult(result, tempSummary);

            data.DelinquentTaxes = tempSummary.DelinquentTaxes;

            if (tempSummary.Errors.ContainsKey("countyClerk"))
                data.Error = tempSummary.Errors["countyClerk"];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch clerk data for {Pin}", pin);
            data.Error = $"Fetch failed: {ex.Message}";
        }

        sw.Stop();
        _logger.LogInformation("PERF: GetClerkDataAsync for {Pin} completed in {Elapsed}ms", pin, sw.ElapsedMilliseconds);

        _cache.Set(cacheKey, data, CacheDuration);
        return new ApiSuccessResponse<ClerkStructuredData> { Data = data };
    }

    public async Task<object> GetRecorderDataAsync(string pin)
    {
        if (!ValidatePin(pin))
            return Error("Invalid or missing PIN. Format: XX-XX-XXX-XXX-XXXX", "INVALID_PIN");

        var cacheKey = $"recorder_{pin}";
        if (_cache.TryGetValue(cacheKey, out RecorderStructuredData? cached) && cached != null)
            return new ApiSuccessResponse<RecorderStructuredData> { Data = cached };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var data = new RecorderStructuredData { Pin = pin };

        try
        {
            var result = await _proxyService.FetchRecorderAsync(pin);
            var tempSummary = new PropertySummaryData { Pin = pin, Errors = new Dictionary<string, string>() };
            ParseRecorderResult(result, tempSummary);

            data.RecorderDocuments = tempSummary.RecorderDocuments;

            if (data.RecorderDocuments?.Documents != null && data.RecorderDocuments.Documents.Count > 0)
            {
                var docList = data.RecorderDocuments.Documents
                    .Where(d => !string.IsNullOrEmpty(d.ViewUrl) && !string.IsNullOrEmpty(d.DocNumber))
                    .Select(d => (docNumber: d.DocNumber!, viewUrl: d.ViewUrl!))
                    .ToList();

                if (docList.Count > 0)
                {
                    var considerations = await _proxyService.FetchRecorderConsiderationsAsync(pin, docList);
                    foreach (var doc in data.RecorderDocuments.Documents)
                    {
                        if (doc.DocNumber != null && considerations.TryGetValue(doc.DocNumber, out var amount))
                            doc.Consideration = amount;
                    }
                }
            }

            if (tempSummary.Errors.ContainsKey("recorder"))
                data.Error = tempSummary.Errors["recorder"];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch recorder data for {Pin}", pin);
            data.Error = $"Fetch failed: {ex.Message}";
        }

        sw.Stop();
        _logger.LogInformation("PERF: GetRecorderDataAsync for {Pin} completed in {Elapsed}ms", pin, sw.ElapsedMilliseconds);

        _cache.Set(cacheKey, data, CacheDuration);
        return new ApiSuccessResponse<RecorderStructuredData> { Data = data };
    }


    public async Task<object> GetCookViewerDataAsync(string pin)
    {
        if (!ValidatePin(pin))
            return Error("Invalid or missing PIN. Format: XX-XX-XXX-XXX-XXXX", "INVALID_PIN");

        var cacheKey = $"cookviewer_{pin}";
        if (_cache.TryGetValue(cacheKey, out CookViewerStructuredData? cached) && cached != null)
            return new ApiSuccessResponse<CookViewerStructuredData> { Data = cached };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var data = new CookViewerStructuredData { Pin = pin };

        try
        {
            var mapData = await FetchCookViewerMapAsync(pin);
            data.CookViewerMap = mapData;

            if (mapData == null)
                data.Error = "Failed to fetch GIS map";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch CookViewer data for {Pin}", pin);
            data.Error = $"Fetch failed: {ex.Message}";
        }

        sw.Stop();
        _logger.LogInformation("PERF: GetCookViewerDataAsync for {Pin} completed in {Elapsed}ms", pin, sw.ElapsedMilliseconds);

        _cache.Set(cacheKey, data, CacheDuration);
        return new ApiSuccessResponse<CookViewerStructuredData> { Data = data };
    }

    public async Task<object> GetGoogleMapsDataAsync(double lat, double lon)
    {
        var cacheKey = $"googlemaps_{lat:F6}_{lon:F6}";
        if (_cache.TryGetValue(cacheKey, out GoogleMapsStructuredData? cached) && cached != null)
            return new ApiSuccessResponse<GoogleMapsStructuredData> { Data = cached };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var data = new GoogleMapsStructuredData { Lat = lat, Lon = lon };

        var apiKey = Environment.GetEnvironmentVariable("GOOGLE_API_KEY");
        if (string.IsNullOrEmpty(apiKey))
        {
            data.Error = "GOOGLE_API_KEY not configured";
            return new ApiSuccessResponse<GoogleMapsStructuredData> { Data = data };
        }

        var client = _httpClientFactory.CreateClient("GIS");

        var satelliteUrl = $"https://maps.googleapis.com/maps/api/staticmap?center={lat},{lon}&zoom=19&size=400x300&maptype=satellite&key={apiKey}";
        var streetViewUrl = $"https://maps.googleapis.com/maps/api/streetview?size=400x300&location={lat},{lon}&fov=90&heading=0&pitch=10&key={apiKey}";

        var satelliteTask = FetchImageAsBase64Async(client, satelliteUrl, "image/png");
        var streetViewTask = FetchImageAsBase64Async(client, streetViewUrl, "image/jpeg");

        await Task.WhenAll(satelliteTask, streetViewTask);

        data.SatelliteImageBase64 = await satelliteTask;
        data.StreetViewImageBase64 = await streetViewTask;

        if (data.SatelliteImageBase64 == null && data.StreetViewImageBase64 == null)
            data.Error = "Failed to fetch Google Maps images";

        sw.Stop();
        _logger.LogInformation("PERF: GetGoogleMapsDataAsync completed in {Elapsed}ms (lat={Lat}, lon={Lon})", sw.ElapsedMilliseconds, lat, lon);

        _cache.Set(cacheKey, data, CacheDuration);
        return new ApiSuccessResponse<GoogleMapsStructuredData> { Data = data };
    }

    private async Task<string?> FetchImageAsBase64Async(HttpClient client, string url, string mimeType)
    {
        try
        {
            var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Google Maps API returned {Status} for {Url}", response.StatusCode, url.Split("&key=")[0]);
                return null;
            }
            var imageBytes = await response.Content.ReadAsByteArrayAsync();
            if (imageBytes.Length < 200)
                return null;
            var contentType = response.Content.Headers.ContentType?.MediaType ?? mimeType;
            return $"data:{contentType};base64,{Convert.ToBase64String(imageBytes)}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch Google Maps image");
            return null;
        }
    }

    private async Task<T> TimedFetch<T>(Func<Task<T>> fetchFunc, string sourceName)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await fetchFunc();
        sw.Stop();
        _logger.LogInformation("PERF: {Source} completed in {Elapsed}ms", sourceName, sw.ElapsedMilliseconds);
        return result;
    }

    private async Task<(object? result, string? error)> SafeFetch<T>(Func<Task<T>> fetchFunc, string sourceName)
    {
        try
        {
            var result = await fetchFunc();
            return (result as object, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch {Source}", sourceName);
            return (null, $"Fetch failed: {ex.Message}");
        }
    }

    private string? ParseTaxPortalResult(object result, PropertySummaryData summary)
    {
        if (result is ApiSuccessResponse<TaxPortalData> success && success.Data?.Html != null)
        {
            try
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(success.Data.Html);

                summary.PropertyInfo = ExtractPropertyInfo(doc);
                summary.Characteristics = ExtractCharacteristics(doc);
                summary.TaxBills = ExtractTaxBills(doc);
                summary.TaxSaleDelinquencies = ExtractTaxSaleDelinquencies(doc);
                summary.PropertyImageBase64 = ExtractPropertyImage(doc);

                return ExtractStreetViewUrl(doc);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse Tax Portal HTML");
                summary.Errors!["taxPortal"] = "Failed to parse tax portal data";
            }
        }
        else
        {
            summary.Errors!["taxPortal"] = "Failed to fetch tax portal data";
        }
        return null;
    }

    private PropertyInfoSection ExtractPropertyInfo(HtmlDocument doc)
    {
        return new PropertyInfoSection
        {
            Address = GetSpanText(doc, "ContentPlaceHolder1_PropertyInfo_propertyAddress"),
            City = GetSpanText(doc, "ContentPlaceHolder1_PropertyInfo_propertyCity"),
            Zip = GetSpanText(doc, "ContentPlaceHolder1_PropertyInfo_propertyZip"),
            Township = GetSpanText(doc, "ContentPlaceHolder1_PropertyInfo_propertyTownship"),
            MailingName = GetSpanText(doc, "ContentPlaceHolder1_PropertyInfo_propertyMailingName"),
            MailingAddress = GetSpanText(doc, "ContentPlaceHolder1_PropertyInfo_propertyMailingAddress"),
            MailingCityStateZip = GetSpanText(doc, "ContentPlaceHolder1_PropertyInfo_propertyMailingCityStateZip"),
        };
    }

    private PropertyCharacteristicsSection ExtractCharacteristics(HtmlDocument doc)
    {
        return new PropertyCharacteristicsSection
        {
            AssessedValue = GetSpanText(doc, "ContentPlaceHolder1_TaxYearInfo_propertyAssessedValue")
                ?? GetSpanText(doc, "ContentPlaceHolder1_TaxYearInfo_lblTaxYearInfoAssessedValue"),
            EstimatedValue = GetSpanText(doc, "ContentPlaceHolder1_TaxYearInfo_propertyEstimatedValue"),
            LotSize = GetSpanText(doc, "ContentPlaceHolder1_TaxYearInfo_propertyLotSize"),
            BuildingSize = GetSpanText(doc, "ContentPlaceHolder1_TaxYearInfo_propertyBuildingSize"),
            PropertyClass = GetSpanText(doc, "ContentPlaceHolder1_TaxYearInfo_propertyClass"),
            PropertyClassDescription = GetSpanText(doc, "ContentPlaceHolder1_TaxYearInfo_msgPropertyClassDescription"),
            TaxRate = GetSpanText(doc, "ContentPlaceHolder1_TaxYearInfo_propertyTaxRate"),
            TaxCode = GetSpanText(doc, "ContentPlaceHolder1_TaxYearInfo_propertyTaxCode"),
            AssessmentPass = GetSpanText(doc, "ContentPlaceHolder1_TaxYearInfo_propertyAssessorPass"),
        };
    }

    private TaxBillSection ExtractTaxBills(HtmlDocument doc)
    {
        var section = new TaxBillSection();

        for (int i = 0; i < 10; i++)
        {
            var year = GetSpanText(doc, $"ContentPlaceHolder1_TaxBillInfo_rptTaxBill_taxBillYear_{i}");
            var amount = GetSpanText(doc, $"ContentPlaceHolder1_TaxBillInfo_rptTaxBill_taxBillAmount_{i}");

            if (string.IsNullOrEmpty(year)) break;

            var entry = new TaxBillEntry
            {
                Year = year.TrimEnd(':'),
                Amount = amount ?? ""
            };

            var panel1 = doc.GetElementbyId($"ContentPlaceHolder1_TaxBillInfo_rptTaxBill_Panel1_{i}");
            var panel3 = doc.GetElementbyId($"ContentPlaceHolder1_TaxBillInfo_rptTaxBill_Panel3_{i}");

            if (panel1 != null)
            {
                var payLink = panel1.SelectSingleNode(".//a[contains(@id,'taxpayonline')]");
                if (payLink != null)
                {
                    var payText = payLink.InnerText.Trim();
                    if (payText.Contains("Pay Online:"))
                    {
                        entry.PaymentStatus = "Balance Due";
                        var amountMatch = Regex.Match(payText, @"\$[\d,]+\.?\d*");
                        if (amountMatch.Success)
                            entry.AmountDue = amountMatch.Value;
                    }
                }
                var paidLink = panel1.SelectSingleNode(".//a[contains(@id,'taxpaid')]");
                if (paidLink != null)
                {
                    entry.PaymentStatus = "Paid";
                }
            }
            else if (panel3 != null)
            {
                var historyLink = panel3.SelectSingleNode(".//a[contains(@id,'taxpaymenthistory')]");
                if (historyLink != null)
                {
                    var histText = historyLink.InnerText.Trim();
                    if (histText.Contains("Paid", StringComparison.OrdinalIgnoreCase))
                    {
                        entry.PaymentStatus = "Paid";
                    }
                    else if (histText.Contains("Balance", StringComparison.OrdinalIgnoreCase) || histText.Contains("Due", StringComparison.OrdinalIgnoreCase))
                    {
                        entry.PaymentStatus = "Balance Due";
                    }
                    else
                    {
                        entry.PaymentStatus = histText;
                    }
                }
            }

            var exemptYear = year.TrimEnd(':');
            for (int e = 0; e < 10; e++)
            {
                var exemptionYearSpan = GetSpanText(doc, $"ContentPlaceHolder1_ExemptionInfo_rptExemptions_exemptionTaxYear_{e}");
                if (exemptionYearSpan != null && exemptionYearSpan.TrimEnd(':') == exemptYear)
                {
                    var exemptPanel = doc.GetElementbyId($"ContentPlaceHolder1_ExemptionInfo_rptExemptions_Panel3_{e}");
                    if (exemptPanel != null)
                    {
                        var exemptLink = exemptPanel.SelectSingleNode(".//a[contains(@id,'exemption')]");
                        if (exemptLink != null)
                        {
                            var exemptText = exemptLink.InnerText.Trim();
                            var numMatch = Regex.Match(exemptText, @"(\d+)\s+Exemption");
                            if (numMatch.Success)
                                entry.ExemptionsReceived = int.Parse(numMatch.Groups[1].Value);
                        }
                    }
                    break;
                }
            }

            section.Bills.Add(entry);
        }

        return section;
    }

    private TaxSaleSection ExtractTaxSaleDelinquencies(HtmlDocument doc)
    {
        var section = new TaxSaleSection();

        for (int i = 0; i < 10; i++)
        {
            var year = GetSpanText(doc, $"ContentPlaceHolder1_RedemptionInfo_rptRedemption_Label2_{i}");
            if (string.IsNullOrEmpty(year)) break;

            var entry = new TaxSaleEntry
            {
                Year = year.TrimEnd(':')
            };

            var panel4 = doc.GetElementbyId($"ContentPlaceHolder1_RedemptionInfo_rptRedemption_Panel4_{i}");
            var panel8 = doc.GetElementbyId($"ContentPlaceHolder1_RedemptionInfo_rptRedemption_Panel8_{i}");
            var panel9 = doc.GetElementbyId($"ContentPlaceHolder1_RedemptionInfo_rptRedemption_Panel9_{i}");

            if (panel4 != null)
            {
                var link = panel4.SelectSingleNode(".//a[contains(@id,'taxsale')]");
                if (link != null)
                {
                    entry.Status = "Sold";
                    entry.Details = link.InnerText.Trim();
                }
            }
            else if (panel8 != null)
            {
                var link = panel8.SelectSingleNode(".//a");
                entry.Status = link != null ? link.InnerText.Trim() : "No Tax Sale";
            }
            else if (panel9 != null)
            {
                var link = panel9.SelectSingleNode(".//a");
                entry.Status = link != null ? link.InnerText.Trim() : "Tax Sale Has Not Occurred";
            }
            else
            {
                entry.Status = "Unknown";
            }

            section.Entries.Add(entry);
        }

        return section;
    }

    private string? ExtractPropertyImage(HtmlDocument doc)
    {
        var imgNode = doc.GetElementbyId("ContentPlaceHolder1_PropertyImage_propertyImage");
        if (imgNode != null)
        {
            var src = imgNode.GetAttributeValue("src", "");
            if (!string.IsNullOrEmpty(src) && src.StartsWith("data:image"))
            {
                return src;
            }
        }
        return null;
    }

    private string? ExtractStreetViewUrl(HtmlDocument doc)
    {
        var imgNode = doc.GetElementbyId("ContentPlaceHolder1_PropertyImage_propertyImage");
        if (imgNode != null)
        {
            var src = imgNode.GetAttributeValue("src", "");
            if (!string.IsNullOrEmpty(src) && src.Contains("maps.googleapis.com"))
            {
                return System.Net.WebUtility.HtmlDecode(src);
            }
        }
        return null;
    }

    private async Task<string?> FetchStreetViewImageAsync(string url)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("GIS");
            var imageBytes = await client.GetByteArrayAsync(url);
            if (imageBytes.Length > 100)
                return $"data:image/jpeg;base64,{Convert.ToBase64String(imageBytes)}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch Street View image");
        }
        return null;
    }

    private void ParseClerkResult(object result, PropertySummaryData summary)
    {
        if (result is ApiSuccessResponse<TaxSearchData> success && success.Data?.Html != null)
        {
            try
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(success.Data.Html);
                summary.DelinquentTaxes = ExtractDelinquentTaxData(doc);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse County Clerk HTML");
                summary.Errors!["countyClerk"] = "Failed to parse delinquent tax data";
            }
        }
        else
        {
            summary.Errors!["countyClerk"] = "Failed to fetch delinquent tax data";
        }
    }

    private DelinquentTaxSection ExtractDelinquentTaxData(HtmlDocument doc)
    {
        var section = new DelinquentTaxSection();

        var dateNode = doc.DocumentNode.SelectSingleNode("//h6[contains(text(),'Data as of')]");
        if (dateNode != null)
        {
            section.DataAsOf = dateNode.InnerText.Replace("Data as of", "").Trim();
        }

        var soldTable = doc.DocumentNode.SelectSingleNode("//div[@id='collapseTwo']//table");
        if (soldTable != null)
        {
            var rows = soldTable.SelectNodes(".//tbody/tr");
            if (rows != null)
            {
                foreach (var row in rows)
                {
                    var cells = row.SelectNodes("td");
                    if (cells == null || cells.Count < 4) continue;

                    section.SoldTaxes.Add(new SoldTaxEntry
                    {
                        TaxSale = cells[0].InnerText.Trim(),
                        FromYearToYear = cells.Count > 1 ? cells[1].InnerText.Trim() : null,
                        Status = cells.Count > 2 ? cells[2].InnerText.Trim() : null,
                        StatusDocNumber = cells.Count > 3 ? cells[3].InnerText.Trim() : null,
                        Date = cells.Count > 4 ? cells[4].InnerText.Trim() : null,
                        Comment = cells.Count > 5 ? cells[5].InnerText.Trim() : null,
                    });
                }
            }

            var footer = soldTable.SelectSingleNode(".//tfoot/tr");
            if (footer != null)
            {
                var footerCells = footer.SelectNodes("td");
                if (footerCells != null && footerCells.Count >= 4)
                {
                    section.TotalTaxBalanceDue1st = footerCells[3].InnerText.Trim();
                    if (footerCells.Count >= 5)
                        section.TotalTaxBalanceDue2nd = footerCells[4].InnerText.Trim();
                }
            }
        }

        var delinquentTable = doc.DocumentNode.SelectSingleNode("//div[@id='collapseThree']//table");
        if (delinquentTable != null)
        {
            var rows = delinquentTable.SelectNodes(".//tbody/tr");
            if (rows != null)
            {
                foreach (var row in rows)
                {
                    var cells = row.SelectNodes("td");
                    if (cells == null || cells.Count < 3) continue;

                    section.DelinquentTaxes.Add(new DelinquentTaxEntry
                    {
                        TaxYear = cells[0].InnerText.Trim(),
                        Status = cells.Count > 1 ? cells[1].InnerText.Trim() : null,
                        ForfeitDate = cells.Count > 2 ? cells[2].InnerText.Trim() : null,
                        FirstInstallmentBalance = cells.Count > 3 ? cells[3].InnerText.Trim() : null,
                        SecondInstallmentBalance = cells.Count > 4 ? cells[4].InnerText.Trim() : null,
                        Type = cells.Count > 5 ? cells[5].InnerText.Trim() : null,
                        WarrantYear = cells.Count > 6 ? cells[6].InnerText.Trim() : null,
                    });
                }
            }
        }

        return section;
    }

    private void ParseRecorderResult(object result, PropertySummaryData summary)
    {
        if (result is ApiSuccessResponse<RecorderData> success && success.Data?.Html != null)
        {
            try
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(success.Data.Html);
                summary.RecorderDocuments = ExtractRecorderDocuments(doc);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse Recorder HTML");
                summary.Errors!["recorder"] = "Failed to parse recorder data";
            }
        }
        else
        {
            summary.Errors!["recorder"] = "Failed to fetch recorder data";
        }
    }

    private RecorderSection ExtractRecorderDocuments(HtmlDocument doc)
    {
        var section = new RecorderSection();

        var totalDocsNode = doc.DocumentNode.SelectSingleNode("//span[@class='text-big-and-bold']");
        if (totalDocsNode != null && int.TryParse(totalDocsNode.InnerText.Trim(), out var total))
        {
            section.TotalDocuments = total;
        }

        var pinNode = doc.DocumentNode.SelectSingleNode("//label[contains(text(),'PIN')]/../../div[2]/span");
        if (pinNode != null) { }

        var addressNode = doc.DocumentNode.SelectSingleNode("//label[contains(text(),'Address')]/../../div[@class='col-md-4'][last()]/span");
        if (addressNode != null)
            section.PropertyAddress = addressNode.InnerText.Trim();

        var cityNode = doc.DocumentNode.SelectSingleNode("//label[contains(text(),'City')]/../../div[@class='col-md-4'][1]/span");
        if (cityNode != null)
            section.City = cityNode.InnerText.Trim();

        var zipNode = doc.DocumentNode.SelectSingleNode("//label[contains(text(),'Zipcode')]/../../div[@class='col-md-4'][last()]/span");
        if (zipNode != null)
            section.Zipcode = zipNode.InnerText.Trim();

        var table = doc.DocumentNode.SelectSingleNode("//table[contains(@class,'table')]");
        if (table != null)
        {
            var rows = table.SelectNodes(".//tbody/tr");
            if (rows != null)
            {
                foreach (var row in rows)
                {
                    var cells = row.SelectNodes("td");
                    if (cells == null || cells.Count < 5) continue;

                    var docEntry = new RecorderDocumentEntry();

                    var viewLink = cells[1].SelectSingleNode(".//a[@target='_blank']");
                    if (viewLink != null)
                        docEntry.ViewUrl = viewLink.GetAttributeValue("href", "");

                    var docNum = cells[2].SelectSingleNode(".//span");
                    if (docNum != null)
                        docEntry.DocNumber = docNum.InnerText.Trim();

                    var dateRec = cells[3].SelectSingleNode(".//span");
                    if (dateRec != null)
                        docEntry.DateRecorded = dateRec.InnerText.Trim();

                    var dateExec = cells[4].SelectSingleNode(".//span");
                    if (dateExec != null)
                        docEntry.DateExecuted = dateExec.InnerText.Trim();

                    var docType = cells.Count > 5 ? cells[5].SelectSingleNode(".//span") : null;
                    if (docType != null)
                        docEntry.DocType = docType.InnerText.Trim();

                    section.Documents.Add(docEntry);
                }
            }
        }

        return section;
    }

    private async Task<CookViewerSection?> FetchCookViewerMapAsync(string pin)
    {
        try
        {
            var cleanPin = pin.Replace("-", "");

            var client = _httpClientFactory.CreateClient("GIS");

            var queryUrl = $"https://gis.cookcountyil.gov/hosting/rest/services/Hosted/Parcel/FeatureServer/0/query?where=name%3D%27{cleanPin}%27&outFields=name&returnGeometry=true&outSR=3857&f=json&resultRecordCount=1";

            _logger.LogInformation("Querying CookViewer parcel for PIN {Pin}", pin);
            var response = await client.GetAsync(queryUrl);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("CookViewer parcel query returned {StatusCode}", response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var jsonDoc = JsonDocument.Parse(json);
            var root = jsonDoc.RootElement;

            if (!root.TryGetProperty("features", out var features) || features.GetArrayLength() == 0)
            {
                _logger.LogWarning("No parcel found for PIN {Pin}", pin);
                return null;
            }

            var feature = features[0];
            var geometry = feature.GetProperty("geometry");

            double xmin = double.MaxValue, ymin = double.MaxValue, xmax = double.MinValue, ymax = double.MinValue;
            var parcelRings = new List<List<double[]>>();
            var parcelRingsWM = new List<List<double[]>>();

            if (geometry.TryGetProperty("rings", out var rings))
            {
                foreach (var ring in rings.EnumerateArray())
                {
                    var latLonRing = new List<double[]>();
                    var wmRing = new List<double[]>();
                    foreach (var coord in ring.EnumerateArray())
                    {
                        var x = coord[0].GetDouble();
                        var y = coord[1].GetDouble();
                        xmin = Math.Min(xmin, x);
                        ymin = Math.Min(ymin, y);
                        xmax = Math.Max(xmax, x);
                        ymax = Math.Max(ymax, y);
                        wmRing.Add(new[] { x, y });
                        var (cLat, cLon) = WebMercatorToLatLon(x, y);
                        latLonRing.Add(new[] { cLat, cLon });
                    }
                    parcelRings.Add(latLonRing);
                    parcelRingsWM.Add(wmRing);
                }
            }

            var dX = xmax - xmin;
            var dY = ymax - ymin;
            var padX = Math.Max(dX * 0.5, 50);
            var padY = Math.Max(dY * 0.5, 50);

            const int mapW = 400;
            const int mapH = 300;
            double targetAspect = (double)mapW / mapH;

            var cx = (xmin + xmax) / 2.0;
            var cy = (ymin + ymax) / 2.0;
            var halfW = dX / 2.0 + padX;
            var halfH = dY / 2.0 + padY;

            double currentAspect = halfW / halfH;
            if (currentAspect > targetAspect)
                halfH = halfW / targetAspect;
            else
                halfW = halfH * targetAspect;

            var bboxXmin = cx - halfW;
            var bboxYmin = cy - halfH;
            var bboxXmax = cx + halfW;
            var bboxYmax = cy + halfH;
            var bbox = $"{bboxXmin},{bboxYmin},{bboxXmax},{bboxYmax}";

            var satelliteUrl = $"https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/export?bbox={bbox}&bboxSR=3857&imageSR=3857&size={mapW},{mapH}&format=jpg&f=image";
            var parcelOverlayUrl = $"https://gis.cookcountyil.gov/traditional/rest/services/cookVwrDynmc/MapServer/export?bbox={bbox}&bboxSR=3857&imageSR=3857&size={mapW},{mapH}&format=png&transparent=true&f=image&layers=show:44";

            var centerX = (xmin + xmax) / 2;
            var centerY = (ymin + ymax) / 2;
            var (lat, lon) = WebMercatorToLatLon(centerX, centerY);

            var satelliteTask = client.GetAsync(satelliteUrl);
            var overlayTask = client.GetAsync(parcelOverlayUrl);

            var apiKey = Environment.GetEnvironmentVariable("GOOGLE_API_KEY");
            Task<string?>? googleSatTask = null;
            Task<string?>? googleSvTask = null;
            if (!string.IsNullOrEmpty(apiKey))
            {
                var gSatUrl = $"https://maps.googleapis.com/maps/api/staticmap?center={lat},{lon}&zoom=19&size=200x150&maptype=satellite&key={apiKey}";
                var gSvUrl = $"https://maps.googleapis.com/maps/api/streetview?size=200x150&location={lat},{lon}&fov=90&heading=0&pitch=10&key={apiKey}";
                googleSatTask = FetchImageAsBase64Async(client, gSatUrl, "image/png");
                googleSvTask = FetchImageAsBase64Async(client, gSvUrl, "image/jpeg");
            }

            var allTasks = new List<Task> { satelliteTask, overlayTask };
            if (googleSatTask != null) allTasks.Add(googleSatTask);
            if (googleSvTask != null) allTasks.Add(googleSvTask);
            await Task.WhenAll(allTasks);

            string? mapImageBase64 = null;
            var satResponse = await satelliteTask;
            if (satResponse.IsSuccessStatusCode)
            {
                var imageBytes = await satResponse.Content.ReadAsByteArrayAsync();
                if (imageBytes.Length > 100)
                    mapImageBase64 = $"data:image/jpeg;base64,{Convert.ToBase64String(imageBytes)}";
            }

            string? parcelOverlayBase64 = null;
            var ovlResponse = await overlayTask;
            if (ovlResponse.IsSuccessStatusCode)
            {
                var imageBytes = await ovlResponse.Content.ReadAsByteArrayAsync();
                if (imageBytes.Length > 100)
                    parcelOverlayBase64 = $"data:image/png;base64,{Convert.ToBase64String(imageBytes)}";
            }

            var section = new CookViewerSection
            {
                MapImageBase64 = mapImageBase64,
                ParcelOverlayBase64 = parcelOverlayBase64,
                CenterLat = lat,
                CenterLon = lon,
                ParcelRings = parcelRings,
                ParcelRingsWebMercator = parcelRingsWM,
                MapBbox = new[] { bboxXmin, bboxYmin, bboxXmax, bboxYmax },
                MapWidth = mapW,
                MapHeight = mapH,
                GoogleSatelliteImageBase64 = googleSatTask != null ? await googleSatTask : null,
                GoogleStreetViewImageBase64 = googleSvTask != null ? await googleSvTask : null,
            };

            _logger.LogInformation("PERF: Google Maps fetched in parallel with GIS imagery (lat={Lat}, lon={Lon})", lat, lon);

            return section;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch CookViewer map for PIN {Pin}", pin);
            return null;
        }
    }

    private static (double lat, double lon) WebMercatorToLatLon(double x, double y)
    {
        var lon = (x / 20037508.34) * 180;
        var lat = (y / 20037508.34) * 180;
        lat = 180 / Math.PI * (2 * Math.Atan(Math.Exp(lat * Math.PI / 180)) - Math.PI / 2);
        return (lat, lon);
    }

    private static string? GetSpanText(HtmlDocument doc, string id)
    {
        var node = doc.GetElementbyId(id);
        if (node == null) return null;
        var text = node.InnerText.Trim();
        return string.IsNullOrEmpty(text) ? null : System.Net.WebUtility.HtmlDecode(text);
    }
}
