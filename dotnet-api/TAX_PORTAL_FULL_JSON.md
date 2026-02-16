# Tax Portal Full JSON Parser

## Overview
This parser extracts **ALL** HTML values from the Cook County Tax Portal into a comprehensive, structured JSON format.

## New Endpoint

### `GET /api/cook/tax-portal-full?pin={PIN}`

Returns all property tax information from cookcountypropertyinfo.com in a fully parsed JSON structure.

#### Example Request:
```
GET http://localhost:5001/api/cook/tax-portal-full?pin=01-01-120-006-0000
```

#### Example Response:
```json
{
  "success": true,
  "data": {
    "pin": "01-01-120-006-0000",
    "propertyAddress": {
      "address": "111 W RUSSELL ST",
      "city": "BARRINGTON",
      "zip": "60010",
      "township": "BARRINGTON"
    },
    "mailingAddress": {
      "name": "JOSEPH D WATKINS",
      "address": "1208 S DIVISION ST",
      "cityStateZip": "BARRINGTON, IL 60010",
      "updateUrl": "https://www.cookcountytreasurer.com/..."
    },
    "propertyCharacteristics": {
      "currentAssessedValue": "32,000",
      "assessmentPass": "(2023 Board Final)",
      "estimatedPropertyValue": "$320,000",
      "assessedValueHistory": [
        { "year": "2023", "value": "32,000" },
        { "year": "2022", "value": "32,000" },
        { "year": "2021", "value": "26,558" }
      ],
      "lotSizeSqFt": "5,976",
      "buildingSqFt": "992",
      "propertyClass": "2-02",
      "propertyClassDescription": "One Story Residence, any age, up to 999 square feet",
      "taxRate": "7.691",
      "taxRateHistory": [
        { "year": "2024", "rate": "7.691" },
        { "year": "2023", "rate": "7.402" }
      ],
      "taxCode": "10021"
    },
    "taxCalculator": {
      "taxYear": "2024",
      "assessedValue": "32,000",
      "stateEqualizationFactor": "3.0355",
      "equalizedAssessedValue": "97,136",
      "localTaxRate": "7.691 %",
      "totalTaxBeforeExemptions": "7,471.04",
      "homeownerExemption": "0.00",
      "seniorCitizenExemption": "0.00",
      "seniorFreezeExemption": "0.00",
      "totalTaxAfterExemptions": "7,471.04"
    },
    "taxBills": [
      {
        "year": "2024",
        "amount": "$7,471.04",
        "status": "Balance Due",
        "payOnlineAmount": "7,848.34"
      },
      {
        "year": "2023",
        "amount": "$7,144.56",
        "status": "Paid in Full"
      }
    ],
    "exemptions": [
      {
        "year": "2024",
        "exemptionsReceived": 0,
        "exemptionTypes": []
      }
    ],
    "appeals": [
      {
        "year": "2024",
        "status": "Not Available",
        "statusMessage": "The tax billing, exemptions and/or appeals process has not started, or information not available for this tax year."
      }
    ],
    "refunds": {
      "status": "No Refund Available",
      "message": "No Refund Available"
    },
    "taxSaleDelinquencies": [
      {
        "year": "2024",
        "status": "Tax Sale Has Not Occurred",
        "statusMessage": "Annual Tax Sale has not occurred."
      }
    ],
    "recordedDocuments": [
      {
        "documentNumber": "1321357082",
        "documentType": "RELEASE",
        "dateRecorded": "08/01/2013"
      },
      {
        "documentNumber": "0318449219",
        "documentType": "RELEASE",
        "dateRecorded": "07/03/2003"
      }
    ],
    "propertyImage": {
      "streetViewUrl": "https://maps.googleapis.com/maps/api/streetview?...",
      "cookViewerUrl": "https://maps.cookcountyil.gov/cookviewer/..."
    },
    "fetchedAt": "2025-01-16T10:30:00.0000000Z"
  }
}
```

## Data Structure

### TaxPortalFullData
Main response object containing all parsed property information:

| Field | Type | Description |
|-------|------|-------------|
| `pin` | string | Property Identification Number |
| `propertyAddress` | PropertyAddressInfo | Physical property address |
| `mailingAddress` | MailingAddressInfo | Owner mailing address |
| `propertyCharacteristics` | PropertyCharacteristicsInfo | Property details and tax info |
| `taxCalculator` | TaxCalculatorInfo | Tax calculation breakdown |
| `taxBills` | TaxBillDetail[] | Historical tax bills |
| `exemptions` | ExemptionDetail[] | Tax exemptions by year |
| `appeals` | AppealDetail[] | Appeal status by year |
| `refunds` | RefundInfo | Refund availability |
| `taxSaleDelinquencies` | TaxSaleDelinquencyDetail[] | Tax sale status |
| `recordedDocuments` | RecordedDocumentDetail[] | Recorded deeds and liens |
| `propertyImage` | PropertyImageInfo | Property images and map URLs |
| `fetchedAt` | string | ISO 8601 timestamp |

### PropertyCharacteristicsInfo
Complete property characteristics including history:

- Current assessed value
- Estimated property value
- **Assessed value history** (6+ years)
- Lot size (sq ft)
- Building size (sq ft)
- Property class and description
- Tax rate
- **Tax rate history** (6+ years)
- Tax code

### TaxBillDetail
Individual tax bill information:

- Year
- Amount billed
- Payment status ("Balance Due", "Paid in Full", "Payment History Available")
- Pay online amount (if balance due)
- Payment URLs

### ExemptionDetail
Tax exemptions by year:

- Year
- Number of exemptions received
- Exemption types
- More info URL

### AppealDetail
Appeal status by year:

- Year
- Status ("Not Available", "Not Accepting", "Accepting", "Filed")
- Status message
- Filing URLs

## Features

✅ **Complete Data Extraction** - Parses ALL HTML values into structured JSON  
✅ **Historical Data** - Includes 6+ years of assessed values and tax rates  
✅ **Payment Status** - Real-time payment and balance information  
✅ **Document Records** - Recorded deeds, mortgages, and liens  
✅ **Tax Calculator** - Full tax calculation breakdown  
✅ **Exemptions & Appeals** - Complete exemption and appeal status  
✅ **Property Images** - Street view and GIS map URLs  

## Usage Example

```csharp
// Fetch comprehensive property data
var response = await httpClient.GetAsync("/api/cook/tax-portal-full?pin=01-01-120-006-0000");
var result = await response.Content.ReadFromJsonAsync<ApiSuccessResponse<TaxPortalFullData>>();

// Access any property information
Console.WriteLine($"Address: {result.Data.PropertyAddress.Address}");
Console.WriteLine($"City: {result.Data.PropertyAddress.City}");
Console.WriteLine($"Assessed Value: {result.Data.PropertyCharacteristics.CurrentAssessedValue}");
Console.WriteLine($"Tax Rate: {result.Data.PropertyCharacteristics.TaxRate}");

// Historical data
foreach (var history in result.Data.PropertyCharacteristics.AssessedValueHistory)
{
    Console.WriteLine($"{history.Year}: {history.Value}");
}

// Tax bills
foreach (var bill in result.Data.TaxBills)
{
    Console.WriteLine($"{bill.Year}: {bill.Amount} - {bill.Status}");
}
```

## Comparison with Other Endpoints

| Endpoint | Data Format | Use Case |
|----------|-------------|----------|
| `/api/cook/tax-portal` | Raw HTML | Display HTML directly |
| `/api/cook/tax-portal-data` | Partial JSON | Basic property info |
| `/api/cook/tax-portal-full` | **Complete JSON** | **Full data extraction** |

## Testing in Swagger

1. Start the application in debug mode
2. Navigate to `http://localhost:5001/`
3. Find the **GET /api/cook/tax-portal-full** endpoint
4. Click "Try it out"
5. Enter a PIN (e.g., `01-01-120-006-0000`)
6. Click "Execute"
7. View the complete JSON response

## Notes

- All data is cached for 5 minutes (same as other endpoints)
- The parser handles missing/unavailable data gracefully
- Historical data availability depends on the source website
- Tax sale and appeal statuses vary by property and tax year
