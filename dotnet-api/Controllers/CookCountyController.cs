using Microsoft.AspNetCore.Mvc;
using CookCountyApi.Models;
using CookCountyApi.Services;

namespace CookCountyApi.Controllers;

[ApiController]
[Route("api/cook")]
public class CookCountyController(
    ICookCountyProxyService proxyService,
    ITaxPortalHtmlParser taxPortalParser,
    IClerkHtmlParser clerkParser,
    IRecorderHtmlParser recorderParser,
    IAssessorHtmlParser assessorParser,
    ITreasurerHtmlParser treasurerParser)
    : ControllerBase
{
    /// <summary>
    /// Get ALL property data from all Cook County sources in one unified response
    /// </summary>
    /// <param name="pin">Property Identification Number (format: XX-XX-XXX-XXX-XXXX)</param>
    [HttpGet("property")]
    [ProducesResponseType(typeof(ApiSuccessResponse<UnifiedPropertyResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetProperty([FromQuery] string pin)
    {
        // Validate PIN format
        if (string.IsNullOrEmpty(pin) || !System.Text.RegularExpressions.Regex.IsMatch(pin, @"^\d{2}-\d{2}-\d{3}-\d{3}-\d{4}$"))
        {
            return BadRequest(new ApiErrorResponse 
            { 
                Error = "Invalid PIN format. Expected: XX-XX-XXX-XXX-XXXX", 
                Code = "INVALID_PIN" 
            });
        }

        var response = new UnifiedPropertyResponse
        {
            Pin = pin,
            FetchedAt = DateTime.UtcNow
        };

        // Fetch all data in parallel
        var tasks = new[]
        {
            FetchTaxPortalData(pin),
            FetchClerkData(pin),
            FetchRecorderData(pin),
            FetchAssessorData(pin),
            FetchTreasurerData(pin)
        };

        var results = await Task.WhenAll(tasks);

        response.TaxPortal = results[0];
        response.Clerk = results[1];
        response.Recorder = results[2];
        response.Assessor = results[3];
        response.Treasurer = results[4];

        return Ok(new ApiSuccessResponse<UnifiedPropertyResponse> { Data = response });
    }

    private async Task<PropertySourceData> FetchTaxPortalData(string pin)
    {
        var source = new PropertySourceData
        {
            Source = "Tax Portal",
            // Tax Portal requires POST, so link to Assessor which shows same data
            SourceUrl = "https://www.cookcountypropertyinfo.com/pinresults.aspx"
        };

        try
        {
            var result = await proxyService.FetchTaxPortalAsync(pin);

            if (result is ApiSuccessResponse<TaxPortalData> { Data: not null } success)
            {
                var parsed = taxPortalParser.ParseTaxPortalHtml(success.Data.Html, pin);
                source.Data = parsed;
            }
            else if (result is ApiErrorResponse error)
            {
                source.Error = error.Error;
            }
        }
        catch (Exception ex)
        {
            source.Error = ex.Message;
        }

        return source;
    }

    private async Task<PropertySourceData> FetchClerkData(string pin)
    {
        var source = new PropertySourceData
        {
            Source = "County Clerk",
            // Clerk requires POST with token, provide search page
            SourceUrl = "https://taxdelinquent.cookcountyclerkil.gov/"
        };

        try
        {
            var result = await proxyService.FetchCountyClerkAsync(pin);

            if (result is ApiSuccessResponse<TaxSearchData> { Data: not null } success)
            {
                var parsed = clerkParser.ParseClerkHtml(success.Data.Html, pin);
                source.Data = parsed;
            }
            else if (result is ApiErrorResponse error)
            {
                source.Error = error.Error;
            }
        }
        catch (Exception ex)
        {
            source.Error = ex.Message;
        }

        return source;
    }

    private async Task<PropertySourceData> FetchRecorderData(string pin)
    {
        var cleanPin = pin.Replace("-", ""); // 01011200060000
        var source = new PropertySourceData
        {
            Source = "Recorder of Deeds",
            // Direct link to documents for this PIN
            SourceUrl = $"https://crs.cookcountyclerkil.gov/Search/ResultByPin?id1={cleanPin}"
        };

        try
        {
            var result = await proxyService.FetchRecorderAsync(pin);

            if (result is ApiSuccessResponse<RecorderData> { Data: not null } success)
            {
                var parsed = recorderParser.ParseRecorderHtml(success.Data.Html, pin);
                source.Data = parsed;
            }
            else if (result is ApiErrorResponse error)
            {
                source.Error = error.Error;
            }
        }
        catch (Exception ex)
        {
            source.Error = ex.Message;
        }

        return source;
    }

    private async Task<PropertySourceData> FetchAssessorData(string pin)
    {
        var cleanPin = pin.Replace("-", ""); // 16063100220000
        var source = new PropertySourceData
        {
            Source = "Assessor's Office",
            // Direct link to property details
            SourceUrl = $"https://www.cookcountyassessoril.gov/pin/{cleanPin}"
        };

        try
        {
            var result = await proxyService.FetchAssessorAsync(pin);

            if (result is ApiSuccessResponse<AssessorData> { Data: not null } success)
            {
                var parsed = assessorParser.ParseAssessorHtml(success.Data.Html, pin);
                source.Data = parsed;
            }
            else if (result is ApiErrorResponse error)
            {
                source.Error = error.Error;
            }
        }
        catch (Exception ex)
        {
            source.Error = ex.Message;
        }

        return source;
    }

    private async Task<PropertySourceData> FetchTreasurerData(string pin)
    {
        var source = new PropertySourceData
        {
            Source = "County Treasurer",
            // Treasurer requires POST with tokens, provide search page
            SourceUrl = "https://www.cookcountytreasurer.com/setsearchparameters.aspx"
        };

        try
        {
            var result = await proxyService.FetchTreasurerAsync(pin);

            if (result is ApiSuccessResponse<TreasurerData> { Data: not null } success)
            {
                var parsed = treasurerParser.ParseTreasurerHtml(success.Data.Html, pin);
                source.Data = parsed;
            }
            else if (result is ApiErrorResponse error)
            {
                source.Error = error.Error;
            }
        }
        catch (Exception ex)
        {
            source.Error = ex.Message;
        }

        return source;
    }

    /// <summary>
    /// Clear cached data for a specific PIN or all cache
    /// </summary>
    [HttpPost("clear-cache")]
    [ProducesResponseType(typeof(CacheClearResponse), StatusCodes.Status200OK)]
    public IActionResult ClearCache([FromQuery] string? pin = null)
    {
        proxyService.ClearCache(pin);
        return Ok(new CacheClearResponse
        {
            Message = string.IsNullOrEmpty(pin) ? "All cache cleared" : $"Cache cleared for PIN: {pin}"
        });
    }
}

[ApiController]
[Route("api")]
public class HealthController : ControllerBase
{
    /// <summary>
    /// Health check endpoint
    /// </summary>
    [HttpGet("health")]
    [ProducesResponseType(typeof(HealthResponse), StatusCodes.Status200OK)]
    public IActionResult GetHealth()
    {
        return Ok(new HealthResponse
        {
            Status = "healthy",
            Timestamp = DateTime.UtcNow.ToString("o"),
            Endpoints = ["/api/cook/property", "/api/cook/clear-cache"]
        });
    }
}
