namespace CookCountyApi.Models;

public class ApiSuccessResponse<T>
{
    public bool Success { get; set; } = true;
    public T? Data { get; set; }
}

public class ApiErrorResponse
{
    public bool Success { get; set; } = false;
    public string Error { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
}

public class TaxPortalData
{
    public string Html { get; set; } = string.Empty;
    public string Pin { get; set; } = string.Empty;
    public string? RedirectUrl { get; set; }
    public string FetchedAt { get; set; } = string.Empty;
}

public class TaxSearchData
{
    public string Html { get; set; } = string.Empty;
    public string Pin { get; set; } = string.Empty;
    public string FetchedAt { get; set; } = string.Empty;
}

public class RecorderData
{
    public string Html { get; set; } = string.Empty;
    public string Pin { get; set; } = string.Empty;
    public string FetchedAt { get; set; } = string.Empty;
}

public class TreasurerData
{
    public string Html { get; set; } = string.Empty;
    public string Pin { get; set; } = string.Empty;
    public string? RedirectUrl { get; set; }
    public string FetchedAt { get; set; } = string.Empty;
}

public class AssessorData
{
    public string Html { get; set; } = string.Empty;
    public string Pin { get; set; } = string.Empty;
    public string FetchedAt { get; set; } = string.Empty;
}

public class HealthResponse
{
    public string Status { get; set; } = "healthy";
    public string Timestamp { get; set; } = string.Empty;
    public string[] Endpoints { get; set; } = Array.Empty<string>();
}

public class CacheClearResponse
{
    public bool Success { get; set; } = true;
    public string Message { get; set; } = string.Empty;
}

public class PropertySummaryData
{
    public string Pin { get; set; } = string.Empty;
    public string FetchedAt { get; set; } = string.Empty;
    public PropertyInfoSection? PropertyInfo { get; set; }
    public PropertyCharacteristicsSection? Characteristics { get; set; }
    public TaxBillSection? TaxBills { get; set; }
    public TaxSaleSection? TaxSaleDelinquencies { get; set; }
    public DelinquentTaxSection? DelinquentTaxes { get; set; }
    public RecorderSection? RecorderDocuments { get; set; }
    public CookViewerSection? CookViewerMap { get; set; }
    public string? PropertyImageBase64 { get; set; }
    public string? StreetViewUrl { get; set; }
    public string? GoogleMapUrl { get; set; }
    public Dictionary<string, string>? Errors { get; set; }
}

public class PropertyInfoSection
{
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Zip { get; set; }
    public string? Township { get; set; }
    public string? MailingName { get; set; }
    public string? MailingAddress { get; set; }
    public string? MailingCityStateZip { get; set; }
}

public class PropertyCharacteristicsSection
{
    public string? AssessedValue { get; set; }
    public string? EstimatedValue { get; set; }
    public string? LotSize { get; set; }
    public string? BuildingSize { get; set; }
    public string? PropertyClass { get; set; }
    public string? PropertyClassDescription { get; set; }
    public string? TaxRate { get; set; }
    public string? TaxCode { get; set; }
    public string? AssessmentPass { get; set; }
}

public class TaxBillSection
{
    public List<TaxBillEntry> Bills { get; set; } = new();
}

public class TaxBillEntry
{
    public string Year { get; set; } = string.Empty;
    public string Amount { get; set; } = string.Empty;
    public string? PaymentStatus { get; set; }
    public string? AmountDue { get; set; }
    public int ExemptionsReceived { get; set; }
}

public class TaxSaleSection
{
    public List<TaxSaleEntry> Entries { get; set; } = new();
}

public class TaxSaleEntry
{
    public string Year { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Details { get; set; }
}

public class DelinquentTaxSection
{
    public List<SoldTaxEntry> SoldTaxes { get; set; } = new();
    public List<DelinquentTaxEntry> DelinquentTaxes { get; set; } = new();
    public string? TotalTaxBalanceDue1st { get; set; }
    public string? TotalTaxBalanceDue2nd { get; set; }
    public string? DataAsOf { get; set; }
}

public class SoldTaxEntry
{
    public string? TaxSale { get; set; }
    public string? FromYearToYear { get; set; }
    public string? Status { get; set; }
    public string? StatusDocNumber { get; set; }
    public string? Date { get; set; }
    public string? Comment { get; set; }
}

public class DelinquentTaxEntry
{
    public string? TaxYear { get; set; }
    public string? Status { get; set; }
    public string? ForfeitDate { get; set; }
    public string? FirstInstallmentBalance { get; set; }
    public string? SecondInstallmentBalance { get; set; }
    public string? Type { get; set; }
    public string? WarrantYear { get; set; }
}

public class RecorderSection
{
    public int TotalDocuments { get; set; }
    public string? PropertyAddress { get; set; }
    public string? City { get; set; }
    public string? Zipcode { get; set; }
    public List<RecorderDocumentEntry> Documents { get; set; } = new();
}

public class RecorderDocumentEntry
{
    public string? DocNumber { get; set; }
    public string? DateRecorded { get; set; }
    public string? DateExecuted { get; set; }
    public string? DocType { get; set; }
    public string? Consideration { get; set; }
    public string? ViewUrl { get; set; }
}

public class TaxPortalStructuredData
{
    public string Pin { get; set; } = string.Empty;
    public PropertyInfoSection? PropertyInfo { get; set; }
    public PropertyCharacteristicsSection? Characteristics { get; set; }
    public TaxBillSection? TaxBills { get; set; }
    public TaxSaleSection? TaxSaleDelinquencies { get; set; }
    public string? PropertyImageBase64 { get; set; }
    public string? Error { get; set; }
}

public class ClerkStructuredData
{
    public string Pin { get; set; } = string.Empty;
    public DelinquentTaxSection? DelinquentTaxes { get; set; }
    public string? Error { get; set; }
}

public class RecorderStructuredData
{
    public string Pin { get; set; } = string.Empty;
    public RecorderSection? RecorderDocuments { get; set; }
    public string? Error { get; set; }
}

public class CookViewerStructuredData
{
    public string Pin { get; set; } = string.Empty;
    public CookViewerSection? CookViewerMap { get; set; }
    public string? Error { get; set; }
}

public class GoogleMapsStructuredData
{
    public string? SatelliteImageBase64 { get; set; }
    public string? StreetViewImageBase64 { get; set; }
    public double Lat { get; set; }
    public double Lon { get; set; }
    public string? Error { get; set; }
}

public class CookViewerSection
{
    public string? MapImageBase64 { get; set; }
    public string? ParcelOverlayBase64 { get; set; }
    public string? MapImageUrl { get; set; }
    public string? ParcelAddress { get; set; }
    public double? CenterLat { get; set; }
    public double? CenterLon { get; set; }
    public List<List<double[]>>? ParcelRings { get; set; }
    public List<List<double[]>>? ParcelRingsWebMercator { get; set; }
    public double[]? MapBbox { get; set; }
    public int MapWidth { get; set; } = 400;
    public int MapHeight { get; set; } = 300;
    public string? GoogleSatelliteImageBase64 { get; set; }
    public string? GoogleStreetViewImageBase64 { get; set; }
}

public class TaxPortalFullData
{
    public string Pin { get; set; } = string.Empty;
    public PropertyAddressInfo? PropertyAddress { get; set; }
    public MailingAddressInfo? MailingAddress { get; set; }
    public PropertyCharacteristicsInfo? PropertyCharacteristics { get; set; }
    public TaxCalculatorInfo? TaxCalculator { get; set; }
    public List<TaxBillDetail> TaxBills { get; set; } = new();
    public List<ExemptionDetail> Exemptions { get; set; } = new();
    public List<AppealDetail> Appeals { get; set; } = new();
    public RefundInfo? Refunds { get; set; }
    public List<TaxSaleDelinquencyDetail> TaxSaleDelinquencies { get; set; } = new();
    public List<RecordedDocumentDetail> RecordedDocuments { get; set; } = new();
    public PropertyImageInfo? PropertyImage { get; set; }
    public string FetchedAt { get; set; } = string.Empty;
}

public class PropertyAddressInfo
{
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Zip { get; set; }
    public string? Township { get; set; }
}

public class MailingAddressInfo
{
    public string? Name { get; set; }
    public string? Address { get; set; }
    public string? CityStateZip { get; set; }
    public string? UpdateUrl { get; set; }
}

public class PropertyCharacteristicsInfo
{
    public string? CurrentAssessedValue { get; set; }
    public string? AssessmentPass { get; set; }
    public string? EstimatedPropertyValue { get; set; }
    public List<AssessedValueHistory> AssessedValueHistory { get; set; } = new();
    public string? LotSizeSqFt { get; set; }
    public string? BuildingSqFt { get; set; }
    public string? PropertyClass { get; set; }
    public string? PropertyClassDescription { get; set; }
    public string? TaxRate { get; set; }
    public List<TaxRateHistory> TaxRateHistory { get; set; } = new();
    public string? TaxCode { get; set; }
    public string? TaxingDistrictsUrl { get; set; }
    public string? TaxRateInfoUrl { get; set; }
}

public class AssessedValueHistory
{
    public string Year { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public class TaxRateHistory
{
    public string Year { get; set; } = string.Empty;
    public string Rate { get; set; } = string.Empty;
}

public class TaxCalculatorInfo
{
    public string? TaxYear { get; set; }
    public string? AssessedValue { get; set; }
    public string? StateEqualizationFactor { get; set; }
    public string? EqualizedAssessedValue { get; set; }
    public string? LocalTaxRate { get; set; }
    public string? TotalTaxBeforeExemptions { get; set; }
    public string? HomeownerExemption { get; set; }
    public string? SeniorCitizenExemption { get; set; }
    public string? SeniorFreezeExemption { get; set; }
    public string? TotalTaxAfterExemptions { get; set; }
}

public class TaxBillDetail
{
    public string Year { get; set; } = string.Empty;
    public string Amount { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? PayOnlineAmount { get; set; }
    public string? PayOnlineUrl { get; set; }
    public string? StatusMessage { get; set; }
    public string? MoreInfoUrl { get; set; }
}

public class ExemptionDetail
{
    public string Year { get; set; } = string.Empty;
    public int ExemptionsReceived { get; set; }
    public List<string> ExemptionTypes { get; set; } = new();
    public string? MoreInfoUrl { get; set; }
}

public class AppealDetail
{
    public string Year { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? StatusMessage { get; set; }
    public List<string>? FilingUrls { get; set; }
}

public class RefundInfo
{
    public string Status { get; set; } = string.Empty;
    public string? Message { get; set; }
    public string? MoreInfoUrl { get; set; }
}

public class TaxSaleDelinquencyDetail
{
    public string Year { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? StatusMessage { get; set; }
    public string? MoreInfoUrl { get; set; }
}

public class RecordedDocumentDetail
{
    public string? DocumentNumber { get; set; }
    public string? DocumentType { get; set; }
    public string? DateRecorded { get; set; }
    public string? MoreRecordsUrl { get; set; }
}

public class PropertyImageInfo
{
    public string? StreetViewUrl { get; set; }
    public string? StreetViewImageBase64 { get; set; }
    public string? CookViewerUrl { get; set; }
}
