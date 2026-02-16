using Microsoft.AspNetCore.Mvc;
using CookCountyApi.Models;
using CookCountyApi.Services;

namespace CookCountyApi.Controllers;

[ApiController]
[Route("api/cook")]
public class CookCountyController : ControllerBase
{
    private readonly ICookCountyProxyService _proxyService;
    private readonly IPropertySummaryService _summaryService;
    private readonly ITaxPortalHtmlParser _htmlParser;

    public CookCountyController(ICookCountyProxyService proxyService, IPropertySummaryService summaryService, ITaxPortalHtmlParser htmlParser)
    {
        _proxyService = proxyService;
        _summaryService = summaryService;
        _htmlParser = htmlParser;
    }

    private void SetNoCacheHeaders()
    {
        Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
        Response.Headers["Pragma"] = "no-cache";
        Response.Headers["Expires"] = "Tue, 01 Jan 2000 00:00:00 GMT";
    }

    /// <summary>
    /// Tab 1: Tax Portal - Fetches tax bill history from cookcountypropertyinfo.com
    /// </summary>
    /// <param name="pin">Property Identification Number (format: XX-XX-XXX-XXX-XXXX)</param>
    /// <returns>Cleaned HTML content with tax history</returns>
    [HttpGet("tax-portal")]
    [ProducesResponseType(typeof(ApiSuccessResponse<TaxPortalData>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> GetTaxPortal([FromQuery] string pin)
    {
        SetNoCacheHeaders();
        var result = await _proxyService.FetchTaxPortalAsync(pin);

        if (result is ApiErrorResponse error)
        {
            var statusCode = error.Code switch
            {
                "INVALID_PIN" => StatusCodes.Status400BadRequest,
                "NOT_FOUND" => StatusCodes.Status404NotFound,
                _ => StatusCodes.Status502BadGateway
            };
            return StatusCode(statusCode, error);
        }

        return Ok(result);
    }

    /// <summary>
    /// Tab 2: County Clerk - Fetches delinquent/sold tax information from taxdelinquent.cookcountyclerkil.gov
    /// </summary>
    /// <param name="pin">Property Identification Number (format: XX-XX-XXX-XXX-XXXX)</param>
    /// <returns>Cleaned HTML with delinquent tax details</returns>
    [HttpGet("county-clerk")]
    [ProducesResponseType(typeof(ApiSuccessResponse<TaxSearchData>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> GetCountyClerk([FromQuery] string pin)
    {
        SetNoCacheHeaders();
        var result = await _proxyService.FetchCountyClerkAsync(pin);

        if (result is ApiErrorResponse error)
        {
            var statusCode = error.Code switch
            {
                "INVALID_PIN" => StatusCodes.Status400BadRequest,
                "NOT_FOUND" => StatusCodes.Status404NotFound,
                _ => StatusCodes.Status502BadGateway
            };
            return StatusCode(statusCode, error);
        }

        return Ok(result);
    }

    /// <summary>
    /// Tab 3: County Treasurer - Fetches payment status from cookcountytreasurer.com
    /// </summary>
    /// <param name="pin">Property Identification Number (format: XX-XX-XXX-XXX-XXXX)</param>
    /// <returns>Redirect HTML for treasurer portal (CAPTCHA protected)</returns>
    [HttpGet("treasurer")]
    [ProducesResponseType(typeof(ApiSuccessResponse<TreasurerData>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> GetTreasurer([FromQuery] string pin)
    {
        SetNoCacheHeaders();
        var result = await _proxyService.FetchTreasurerAsync(pin);

        if (result is ApiErrorResponse error)
        {
            var statusCode = error.Code switch
            {
                "INVALID_PIN" => StatusCodes.Status400BadRequest,
                "NOT_FOUND" => StatusCodes.Status404NotFound,
                _ => StatusCodes.Status502BadGateway
            };
            return StatusCode(statusCode, error);
        }

        return Ok(result);
    }

    /// <summary>
    /// Tab 4: Recorder - Fetches recorded documents from crs.cookcountyclerkil.gov
    /// </summary>
    /// <param name="pin">Property Identification Number (format: XX-XX-XXX-XXX-XXXX)</param>
    /// <returns>HTML with list of recorded documents</returns>
    [HttpGet("recorder")]
    [ProducesResponseType(typeof(ApiSuccessResponse<RecorderData>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> GetRecorder([FromQuery] string pin)
    {
        SetNoCacheHeaders();
        var result = await _proxyService.FetchRecorderAsync(pin);

        if (result is ApiErrorResponse error)
        {
            var statusCode = error.Code switch
            {
                "INVALID_PIN" => StatusCodes.Status400BadRequest,
                "NOT_FOUND" => StatusCodes.Status404NotFound,
                _ => StatusCodes.Status502BadGateway
            };
            return StatusCode(statusCode, error);
        }

        return Ok(result);
    }

    /// <summary>
    /// Tab 5: Assessor - Fetches property details from cookcountyassessoril.gov
    /// </summary>
    /// <param name="pin">Property Identification Number (format: XX-XX-XXX-XXX-XXXX)</param>
    /// <returns>HTML with property details and assessed values</returns>
    [HttpGet("assessor")]
    [ProducesResponseType(typeof(ApiSuccessResponse<AssessorData>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> GetAssessor([FromQuery] string pin)
    {
        SetNoCacheHeaders();
        var result = await _proxyService.FetchAssessorAsync(pin);

        if (result is ApiErrorResponse error)
        {
            var statusCode = error.Code switch
            {
                "INVALID_PIN" => StatusCodes.Status400BadRequest,
                "NOT_FOUND" => StatusCodes.Status404NotFound,
                _ => StatusCodes.Status502BadGateway
            };
            return StatusCode(statusCode, error);
        }

        return Ok(result);
    }

    /// <summary>
    /// Property Summary - All data from all sources in one structured response
    /// </summary>
    /// <param name="pin">Property Identification Number (format: XX-XX-XXX-XXX-XXXX)</param>
    [HttpGet("property-summary")]
    [ProducesResponseType(typeof(ApiSuccessResponse<PropertySummaryData>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> GetPropertySummary([FromQuery] string pin)
    {
        SetNoCacheHeaders();
        var result = await _summaryService.GetPropertySummaryAsync(pin);

        if (result is ApiErrorResponse error)
        {
            var statusCode = error.Code switch
            {
                "INVALID_PIN" => StatusCodes.Status400BadRequest,
                _ => StatusCodes.Status502BadGateway
            };
            return StatusCode(statusCode, error);
        }

        return Ok(result);
    }

    /// <summary>
    /// Tax Portal structured data - Returns parsed property info, characteristics, tax bills, and property image
    /// </summary>
    [HttpGet("tax-portal-data")]
    [ProducesResponseType(typeof(ApiSuccessResponse<TaxPortalStructuredData>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> GetTaxPortalData([FromQuery] string pin)
    {
        SetNoCacheHeaders();
        var result = await _summaryService.GetTaxPortalDataAsync(pin);

        if (result is ApiErrorResponse error)
        {
            var statusCode = error.Code switch
            {
                "INVALID_PIN" => StatusCodes.Status400BadRequest,
                _ => StatusCodes.Status502BadGateway
            };
            return StatusCode(statusCode, error);
        }

        return Ok(result);
    }

    /// <summary>
    /// Clerk structured data - Returns parsed delinquent tax information
    /// </summary>
    [HttpGet("clerk-data")]
    [ProducesResponseType(typeof(ApiSuccessResponse<ClerkStructuredData>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> GetClerkData([FromQuery] string pin)
    {
        SetNoCacheHeaders();
        var result = await _summaryService.GetClerkDataAsync(pin);

        if (result is ApiErrorResponse error)
        {
            var statusCode = error.Code switch
            {
                "INVALID_PIN" => StatusCodes.Status400BadRequest,
                _ => StatusCodes.Status502BadGateway
            };
            return StatusCode(statusCode, error);
        }

        return Ok(result);
    }

    /// <summary>
    /// Recorder structured data - Returns parsed recorded documents
    /// </summary>
    [HttpGet("recorder-data")]
    [ProducesResponseType(typeof(ApiSuccessResponse<RecorderStructuredData>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> GetRecorderData([FromQuery] string pin)
    {
        SetNoCacheHeaders();
        var result = await _summaryService.GetRecorderDataAsync(pin);

        if (result is ApiErrorResponse error)
        {
            var statusCode = error.Code switch
            {
                "INVALID_PIN" => StatusCodes.Status400BadRequest,
                _ => StatusCodes.Status502BadGateway
            };
            return StatusCode(statusCode, error);
        }

        return Ok(result);
    }

    /// <summary>
    /// CookViewer structured data - Returns GIS map data with parcel geometry
    /// </summary>
    [HttpGet("cookviewer-data")]
    [ProducesResponseType(typeof(ApiSuccessResponse<CookViewerStructuredData>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> GetCookViewerData([FromQuery] string pin)
    {
        SetNoCacheHeaders();
        var result = await _summaryService.GetCookViewerDataAsync(pin);

        if (result is ApiErrorResponse error)
        {
            var statusCode = error.Code switch
            {
                "INVALID_PIN" => StatusCodes.Status400BadRequest,
                _ => StatusCodes.Status502BadGateway
            };
            return StatusCode(statusCode, error);
        }

        return Ok(result);
    }

    /// <summary>
    /// Google Maps structured data - Returns satellite and street view images
    /// </summary>
    [HttpGet("google-maps-data")]
    [ProducesResponseType(typeof(ApiSuccessResponse<GoogleMapsStructuredData>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetGoogleMapsData([FromQuery] double lat, [FromQuery] double lon)
    {
        SetNoCacheHeaders();

        if (lat == 0 && lon == 0)
            return BadRequest(new ApiErrorResponse { Error = "lat and lon are required", Code = "INVALID_PARAMS" });

        var result = await _summaryService.GetGoogleMapsDataAsync(lat, lon);
        return Ok(result);
    }

    /// <summary>
    /// Tax Portal Full JSON - Returns ALL parsed HTML values in comprehensive JSON format
    /// </summary>
    [HttpGet("tax-portal-full")]
    [ProducesResponseType(typeof(ApiSuccessResponse<TaxPortalFullData>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> GetTaxPortalFull([FromQuery] string pin)
    {
        SetNoCacheHeaders();
        
        var result = await _proxyService.FetchTaxPortalAsync(pin);

        if (result is ApiErrorResponse error)
        {
            var statusCode = error.Code switch
            {
                "INVALID_PIN" => StatusCodes.Status400BadRequest,
                "NOT_FOUND" => StatusCodes.Status404NotFound,
                _ => StatusCodes.Status502BadGateway
            };
            return StatusCode(statusCode, error);
        }

        if (result is ApiSuccessResponse<TaxPortalData> success && success.Data != null)
        {
            var parsedData = _htmlParser.ParseTaxPortalHtml(success.Data.Html, pin);
            return Ok(new ApiSuccessResponse<TaxPortalFullData> { Data = parsedData });
        }

        return StatusCode(StatusCodes.Status502BadGateway, new ApiErrorResponse 
        { 
            Error = "Failed to parse tax portal data", 
            Code = "PARSE_ERROR" 
        });
    }

    /// <summary>
    /// Clears cached responses
    /// </summary>
    /// <param name="pin">Optional: specific PIN to clear from cache</param>
    [HttpPost("clear-cache")]
    [ProducesResponseType(typeof(CacheClearResponse), StatusCodes.Status200OK)]
    public IActionResult ClearCache([FromQuery] string? pin = null)
    {
        _proxyService.ClearCache(pin);
        return Ok(new CacheClearResponse
        {
            Message = string.IsNullOrEmpty(pin)
                ? "All cache cleared"
                : $"Cache cleared for PIN: {pin}"
        });
    }
}

[ApiController]
[Route("api")]
public class HealthController : ControllerBase
{
    /// <summary>
    /// Health check endpoint for monitoring
    /// </summary>
    [HttpGet("health")]
    [ProducesResponseType(typeof(HealthResponse), StatusCodes.Status200OK)]
    public IActionResult GetHealth()
    {
        return Ok(new HealthResponse
        {
            Status = "healthy",
            Timestamp = DateTime.UtcNow.ToString("o"),
            Endpoints = new[]
            {
                "/api/cook/tax-portal",
                "/api/cook/county-clerk",
                "/api/cook/treasurer",
                "/api/cook/recorder",
                "/api/cook/assessor",
                "/api/cook/property-summary",
                "/api/cook/tax-portal-data",
                "/api/cook/tax-portal-full",
                "/api/cook/clerk-data",
                "/api/cook/recorder-data",
                "/api/cook/cookviewer-data",
                "/api/cook/google-maps-data"
            }
        });
    }
}
