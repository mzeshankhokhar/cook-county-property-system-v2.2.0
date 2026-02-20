# Cook County API - Comprehensive JSON Parser Endpoints

## Overview
All Cook County HTML data sources now have comprehensive JSON parser endpoints that extract **ALL** HTML values into structured JSON format.

## New Endpoints Summary

| Endpoint | Description | Returns |
|----------|-------------|---------|
| `/api/cook/tax-portal-full` | Complete Tax Portal data | TaxPortalFullData |
| `/api/cook/clerk-full` | Complete County Clerk data | ClerkFullData |
| `/api/cook/recorder-full` | Complete Recorder data | RecorderFullData |
| `/api/cook/assessor-full` | Complete Assessor data | AssessorFullData |
| `/api/cook/treasurer-full` | Complete Treasurer data | TreasurerFullData |

---

## 1. Tax Portal Full JSON
**Endpoint:** `GET /api/cook/tax-portal-full?pin={PIN}`

### Data Extracted:
- ✅ Property & Mailing Addresses
- ✅ Current & Historical Assessed Values (6+ years)
- ✅ Property Characteristics (lot size, building size, class)
- ✅ Tax Rates & History (6+ years)
- ✅ Tax Calculator breakdown
- ✅ Tax Bills with payment status (6+ years)
- ✅ Exemptions by year
- ✅ Appeals status by year
- ✅ Refund information
- ✅ Tax Sale Delinquencies
- ✅ Recorded Documents
- ✅ Property Images URLs

### Example Response:
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
    "propertyCharacteristics": {
      "currentAssessedValue": "32,000",
      "estimatedPropertyValue": "$320,000",
      "assessedValueHistory": [
        { "year": "2023", "value": "32,000" },
        { "year": "2022", "value": "32,000" }
      ],
      "taxRateHistory": [
        { "year": "2024", "rate": "7.691" }
      ]
    },
    "taxBills": [
      {
        "year": "2024",
        "amount": "$7,471.04",
        "status": "Balance Due",
        "payOnlineAmount": "7,848.34"
      }
    ]
  }
}
```

---

## 2. Clerk Full JSON
**Endpoint:** `GET /api/cook/clerk-full?pin={PIN}`

### Data Extracted:
- ✅ Data as of date
- ✅ Sold Tax Information
  - Tax sale details
  - Tax year ranges
  - Status and document numbers
  - Total balances (1st & 2nd installments)
- ✅ Delinquent Tax Information
  - Tax year
  - Status
  - Forfeit dates
  - Installment balances
  - Warrant information

### Example Response:
```json
{
  "success": true,
  "data": {
    "pin": "01-01-120-006-0000",
    "dataAsOf": "01/15/2025",
    "soldTaxInfo": {
      "soldTaxes": [
        {
          "taxSale": "2023",
          "taxYearRange": "2020-2021",
          "status": "Redeemed",
          "statusDocNumber": "123456789",
          "date": "12/01/2023"
        }
      ],
      "totalBalance1stInstallment": "0.00",
      "totalBalance2ndInstallment": "0.00"
    },
    "delinquentTaxInfo": {
      "delinquentTaxes": [
        {
          "taxYear": "2024",
          "status": "Current",
          "firstInstallmentBalance": "0.00",
          "secondInstallmentBalance": "3,924.17"
        }
      ]
    },
    "fetchedAt": "2025-01-16T10:30:00Z"
  }
}
```

---

## 3. Recorder Full JSON
**Endpoint:** `GET /api/cook/recorder-full?pin={PIN}`

### Data Extracted:
- ✅ Property Information (address, city, zip)
- ✅ All Recorded Documents
  - Document numbers
  - Recording dates
  - Execution dates
  - Document types (deed, mortgage, release, etc.)
  - Consideration amounts
  - View/Download URLs
- ✅ Total document count

### Example Response:
```json
{
  "success": true,
  "data": {
    "pin": "01-01-120-006-0000",
    "propertyInfo": {
      "address": "111 W RUSSELL ST",
      "city": "BARRINGTON",
      "zipcode": "60010"
    },
    "documents": [
      {
        "documentNumber": "1321357082",
        "dateRecorded": "08/01/2013",
        "dateExecuted": "07/25/2013",
        "documentType": "RELEASE",
        "consideration": "$0",
        "viewUrl": "https://crs.cookcountyclerkil.gov/..."
      },
      {
        "documentNumber": "92895329",
        "dateRecorded": "11/30/1992",
        "documentType": "WARRANTY DEED",
        "consideration": "$250,000"
      }
    ],
    "totalDocuments": 5,
    "fetchedAt": "2025-01-16T10:30:00Z"
  }
}
```

---

## 4. Assessor Full JSON
**Endpoint:** `GET /api/cook/assessor-full?pin={PIN}`

### Data Extracted:
- ✅ Property Information
  - Address, city, zip
  - Township, neighborhood
  - Tax code
- ✅ Valuation Information
  - Current & prior assessed values
  - Market value
  - Assessment level
  - Property class & description
- ✅ Building Information
  - Year built
  - Square footage
  - Stories, rooms, bedrooms, bathrooms
  - Garage, basement
  - Exterior construction
  - Roof material
- ✅ Land Information
  - Land square feet
  - Land use
  - Topography
- ✅ Appeal History
- ✅ Exemption History

### Example Response:
```json
{
  "success": true,
  "data": {
    "pin": "01-01-120-006-0000",
    "propertyInfo": {
      "propertyAddress": "111 W RUSSELL ST",
      "city": "BARRINGTON",
      "zip": "60010",
      "township": "BARRINGTON",
      "taxCode": "10021"
    },
    "valuationInfo": {
      "currentAssessedValue": "32,000",
      "priorAssessedValue": "32,000",
      "marketValue": "320,000",
      "propertyClass": "2-02",
      "propertyClassDescription": "One Story Residence"
    },
    "buildingInfo": {
      "yearBuilt": "1950",
      "buildingSquareFeet": "992",
      "stories": "1",
      "bedrooms": "2",
      "bathrooms": "1"
    },
    "landInfo": {
      "landSquareFeet": "5,976"
    },
    "fetchedAt": "2025-01-16T10:30:00Z"
  }
}
```

---

## 5. Treasurer Full JSON
**Endpoint:** `GET /api/cook/treasurer-full?pin={PIN}`

### Data Extracted:
- ✅ Property Information
  - Address
  - Owner name
  - Taxpayer name
- ✅ Tax Bills by year
  - Total amount
  - Amount paid
  - Amount due
  - Installment due dates
  - Status
- ✅ Payment Summary
  - Total billed
  - Total paid
  - Total due
  - Last payment date & amount
- ✅ Redirect URL (for CAPTCHA-protected pages)

### Example Response:
```json
{
  "success": true,
  "data": {
    "pin": "01-01-120-006-0000",
    "propertyInfo": {
      "address": "111 W RUSSELL ST, BARRINGTON",
      "owner": "JOSEPH D WATKINS",
      "taxpayerName": "WATKINS, JOSEPH D"
    },
    "taxBills": [
      {
        "taxYear": "2024",
        "totalAmount": "$7,471.04",
        "amountPaid": "$0.00",
        "amountDue": "$7,848.34",
        "status": "Balance Due"
      },
      {
        "taxYear": "2023",
        "totalAmount": "$7,144.56",
        "amountPaid": "$7,144.56",
        "amountDue": "$0.00",
        "status": "Paid in Full"
      }
    ],
    "paymentSummary": {
      "totalBilled": "$14,615.60",
      "totalPaid": "$7,144.56",
      "totalDue": "$7,848.34",
      "lastPaymentDate": "03/15/2024",
      "lastPaymentAmount": "$3,572.28"
    },
    "redirectUrl": "https://www.cookcountytreasurer.com/...",
    "fetchedAt": "2025-01-16T10:30:00Z"
  }
}
```

---

## Comparison: Original vs Full Endpoints

| Original Endpoint | Data Format | Full Endpoint | Data Format |
|-------------------|-------------|---------------|-------------|
| `/api/cook/tax-portal` | Raw HTML | `/api/cook/tax-portal-full` | Complete JSON |
| `/api/cook/county-clerk` | Raw HTML | `/api/cook/clerk-full` | Complete JSON |
| `/api/cook/recorder` | Raw HTML | `/api/cook/recorder-full` | Complete JSON |
| `/api/cook/assessor` | Raw HTML | `/api/cook/assessor-full` | Complete JSON |
| `/api/cook/treasurer` | Raw HTML | `/api/cook/treasurer-full` | Complete JSON |

---

## Features

✅ **Complete Data Extraction** - ALL HTML values parsed into structured JSON  
✅ **HTML Entity Decoding** - Properly handles `&nbsp;`, `&amp;`, etc.  
✅ **Historical Data** - Includes multi-year historical information  
✅ **Clean Data** - Whitespace normalized, values trimmed  
✅ **Type Safety** - Strongly typed models for all data  
✅ **Swagger Documentation** - Full API documentation  
✅ **Consistent Format** - All endpoints follow the same pattern  

---

## Testing All Endpoints

### Using Swagger UI:
1. Start your application (F5)
2. Navigate to `http://localhost:5001/`
3. Find any of the `-full` endpoints
4. Click "Try it out"
5. Enter PIN: `01-01-120-006-0000`
6. Click "Execute"
7. View comprehensive JSON response

### Using cURL:

```bash
# Tax Portal Full
curl "http://localhost:5001/api/cook/tax-portal-full?pin=01-01-120-006-0000"

# Clerk Full
curl "http://localhost:5001/api/cook/clerk-full?pin=01-01-120-006-0000"

# Recorder Full
curl "http://localhost:5001/api/cook/recorder-full?pin=01-01-120-006-0000"

# Assessor Full
curl "http://localhost:5001/api/cook/assessor-full?pin=01-01-120-006-0000"

# Treasurer Full
curl "http://localhost:5001/api/cook/treasurer-full?pin=01-01-120-006-0000"
```

### Using JavaScript/Fetch:

```javascript
// Fetch all data sources for a property
const pin = "01-01-120-006-0000";

const [taxPortal, clerk, recorder, assessor, treasurer] = await Promise.all([
  fetch(`/api/cook/tax-portal-full?pin=${pin}`).then(r => r.json()),
  fetch(`/api/cook/clerk-full?pin=${pin}`).then(r => r.json()),
  fetch(`/api/cook/recorder-full?pin=${pin}`).then(r => r.json()),
  fetch(`/api/cook/assessor-full?pin=${pin}`).then(r => r.json()),
  fetch(`/api/cook/treasurer-full?pin=${pin}`).then(r => r.json())
]);

console.log("Tax Portal:", taxPortal.data);
console.log("Clerk:", clerk.data);
console.log("Recorder:", recorder.data);
console.log("Assessor:", assessor.data);
console.log("Treasurer:", treasurer.data);
```

---

## Error Handling

All endpoints return consistent error responses:

```json
{
  "success": false,
  "error": "Invalid or missing PIN. Format: XX-XX-XXX-XXX-XXXX",
  "code": "INVALID_PIN"
}
```

### Error Codes:
- `INVALID_PIN` - PIN format is incorrect (400 Bad Request)
- `NOT_FOUND` - Property not found (404 Not Found)
- `PARSE_ERROR` - Failed to parse HTML (502 Bad Gateway)
- `FETCH_ERROR` - Failed to fetch data from source (502 Bad Gateway)

---

## Performance Notes

- All endpoints use the same caching as original endpoints (5 minutes)
- Parsing is done synchronously after fetching
- Minimal performance overhead compared to HTML endpoints
- Historical data parsing may vary based on HTML complexity

---

## Next Steps

1. **Test all endpoints** with various PINs
2. **Integrate into your frontend** application
3. **Build property detail views** using the structured JSON
4. **Cache responses** in your application if needed
5. **Handle errors** gracefully in your UI

---

## Support

For questions or issues:
1. Check Swagger UI documentation at `http://localhost:5001/`
2. Review API response error messages
3. Check logs for detailed error information
4. Test with the health endpoint: `GET /api/health`
