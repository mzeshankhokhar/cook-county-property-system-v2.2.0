using System.Net;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using CookCountyApi.Models;

namespace CookCountyApi.Services;

public interface ITaxPortalHtmlParser
{
    TaxPortalFullData ParseTaxPortalHtml(string html, string pin);
}

public class TaxPortalHtmlParser : ITaxPortalHtmlParser
{
    public TaxPortalFullData ParseTaxPortalHtml(string html, string pin)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var data = new TaxPortalFullData
        {
            Pin = pin,
            FetchedAt = DateTime.UtcNow.ToString("o")
        };

        // Parse Property Address
        data.PropertyAddress = ParsePropertyAddress(doc);

        // Parse Mailing Address
        data.MailingAddress = ParseMailingAddress(doc);

        // Parse Property Characteristics
        data.PropertyCharacteristics = ParsePropertyCharacteristics(doc);

        // Parse Tax Calculator
        data.TaxCalculator = ParseTaxCalculator(doc);

        // Parse Tax Bills
        data.TaxBills = ParseTaxBills(doc);

        // Parse Exemptions
        data.Exemptions = ParseExemptions(doc);

        // Parse Appeals
        data.Appeals = ParseAppeals(doc);

        // Parse Refunds
        data.Refunds = ParseRefunds(doc);

        // Parse Tax Sale Delinquencies
        data.TaxSaleDelinquencies = ParseTaxSaleDelinquencies(doc);

        // Parse Recorded Documents
        data.RecordedDocuments = ParseRecordedDocuments(doc);

        // Parse Property Image
        data.PropertyImage = ParsePropertyImage(doc);

        return data;
    }

    private PropertyAddressInfo? ParsePropertyAddress(HtmlDocument doc)
    {
        var info = new PropertyAddressInfo
        {
            Address = GetElementValue(doc, "ContentPlaceHolder1_PropertyInfo_propertyAddress"),
            City = GetElementValue(doc, "ContentPlaceHolder1_PropertyInfo_propertyCity"),
            Zip = GetElementValue(doc, "ContentPlaceHolder1_PropertyInfo_propertyZip"),
            Township = GetElementValue(doc, "ContentPlaceHolder1_PropertyInfo_propertyTownship")
        };

        return HasAnyValue(info) ? info : null;
    }

    private MailingAddressInfo? ParseMailingAddress(HtmlDocument doc)
    {
        var info = new MailingAddressInfo
        {
            Name = GetElementValue(doc, "ContentPlaceHolder1_PropertyInfo_propertyMailingName"),
            Address = GetElementValue(doc, "ContentPlaceHolder1_PropertyInfo_propertyMailingAddress"),
            CityStateZip = GetElementValue(doc, "ContentPlaceHolder1_PropertyInfo_propertyMailingCityStateZip"),
            UpdateUrl = GetLinkHref(doc, "ContentPlaceHolder1_PropertyInfo_lnkNameMailingAddress")
        };

        return HasAnyValue(info) ? info : null;
    }

    private PropertyCharacteristicsInfo? ParsePropertyCharacteristics(HtmlDocument doc)
    {
        var info = new PropertyCharacteristicsInfo
        {
            CurrentAssessedValue = GetElementValue(doc, "ContentPlaceHolder1_TaxYearInfo_lblTaxYearInfoAssessedValue"),
            AssessmentPass = GetElementValue(doc, "ContentPlaceHolder1_TaxYearInfo_propertyAssessorPass"),
            EstimatedPropertyValue = GetElementValue(doc, "ContentPlaceHolder1_TaxYearInfo_propertyEstimatedValue"),
            LotSizeSqFt = GetElementValue(doc, "ContentPlaceHolder1_TaxYearInfo_propertyLotSize"),
            BuildingSqFt = GetElementValue(doc, "ContentPlaceHolder1_TaxYearInfo_propertyBuildingSize"),
            PropertyClass = GetElementValue(doc, "ContentPlaceHolder1_TaxYearInfo_propertyClass"),
            PropertyClassDescription = GetElementValue(doc, "ContentPlaceHolder1_TaxYearInfo_msgPropertyClassDescription"),
            TaxRate = GetElementValue(doc, "ContentPlaceHolder1_TaxYearInfo_propertyTaxRate"),
            TaxCode = GetElementValue(doc, "ContentPlaceHolder1_TaxYearInfo_propertyTaxCode"),
            TaxingDistrictsUrl = GetLinkHref(doc, "ContentPlaceHolder1_TaxYearInfo_lnkTaxingDistrict"),
            TaxRateInfoUrl = "https://www.cookcountyclerk.com/service/tax-extension-and-rates"
        };

        // Parse Assessed Value History
        info.AssessedValueHistory = ParseAssessedValueHistory(doc);

        // Parse Tax Rate History
        info.TaxRateHistory = ParseTaxRateHistory(doc);

        return HasAnyValue(info) ? info : null;
    }

    private List<AssessedValueHistory> ParseAssessedValueHistory(HtmlDocument doc)
    {
        var history = new List<AssessedValueHistory>();
        var historyTable = doc.DocumentNode.SelectSingleNode("//table[@id='assessdhistorytable']");

        if (historyTable != null)
        {
            var rows = historyTable.SelectNodes(".//tr");
            if (rows != null)
            {
                foreach (var row in rows)
                {
                    var cells = row.SelectNodes(".//td");
                    if (cells?.Count >= 2)
                    {
                        history.Add(new AssessedValueHistory
                        {
                            Year = CleanText(cells[0].InnerText),
                            Value = CleanText(cells[1].InnerText)
                        });
                    }
                }
            }
        }

        return history;
    }

    private List<TaxRateHistory> ParseTaxRateHistory(HtmlDocument doc)
    {
        var history = new List<TaxRateHistory>();
        var historyTable = doc.DocumentNode.SelectSingleNode("//table[@id='taxratehistorytable']");

        if (historyTable != null)
        {
            var rows = historyTable.SelectNodes(".//tr");
            if (rows != null)
            {
                foreach (var row in rows)
                {
                    var cell = row.SelectSingleNode(".//td");
                    if (cell != null)
                    {
                        var text = CleanText(cell.InnerText);
                        // Parse format: "2024   7.691"
                        var match = Regex.Match(text, @"(\d{4})\s+([\d.]+)");
                        if (match.Success)
                        {
                            history.Add(new TaxRateHistory
                            {
                                Year = match.Groups[1].Value,
                                Rate = match.Groups[2].Value
                            });
                        }
                    }
                }
            }
        }

        return history;
    }

    private TaxCalculatorInfo? ParseTaxCalculator(HtmlDocument doc)
    {
        var info = new TaxCalculatorInfo
        {
            TaxYear = "2024",
            AssessedValue = GetElementValue(doc, "ContentPlaceHolder1_TaxCalculator2_lblAssessedValue"),
            StateEqualizationFactor = GetElementValue(doc, "ContentPlaceHolder1_TaxCalculator2_lblEqualizationFactor"),
            EqualizedAssessedValue = GetElementValue(doc, "ContentPlaceHolder1_TaxCalculator2_lblEqualizedValue"),
            LocalTaxRate = GetElementValue(doc, "ContentPlaceHolder1_TaxCalculator2_lblLocalTaxRate"),
            TotalTaxBeforeExemptions = GetElementValue(doc, "ContentPlaceHolder1_TaxCalculator2_lblTaxBeforeExemptions"),
            HomeownerExemption = GetElementValue(doc, "ContentPlaceHolder1_TaxCalculator2_lblHomeownerExemption"),
            SeniorCitizenExemption = GetElementValue(doc, "ContentPlaceHolder1_TaxCalculator2_lblSeniorCitizenExemption"),
            SeniorFreezeExemption = GetElementValue(doc, "ContentPlaceHolder1_TaxCalculator2_lblSeniorFreezeExemption"),
            TotalTaxAfterExemptions = GetElementValue(doc, "ContentPlaceHolder1_TaxCalculator2_lblTaxAfterExemptions")
        };

        return HasAnyValue(info) ? info : null;
    }

    private List<TaxBillDetail> ParseTaxBills(HtmlDocument doc)
    {
        var bills = new List<TaxBillDetail>();

        // Parse tax bill repeater items
        for (int i = 0; i < 10; i++)
        {
            var year = GetElementValue(doc, $"ContentPlaceHolder1_TaxBillInfo_rptTaxBill_taxBillYear_{i}");
            if (string.IsNullOrEmpty(year)) break;

            var bill = new TaxBillDetail
            {
                Year = year.TrimEnd(':'),
                Amount = GetElementValue(doc, $"ContentPlaceHolder1_TaxBillInfo_rptTaxBill_taxBillAmount_{i}") ?? "",
                MoreInfoUrl = "https://www.cookcountytreasurer.com"
            };

            // Try to get Pay Online amount and status
            var payOnlineNode = doc.DocumentNode.SelectSingleNode($"//a[@id='taxpayonline2{bill.Year}-button']//span");
            if (payOnlineNode != null)
            {
                var payText = CleanText(payOnlineNode.InnerText);
                var match = Regex.Match(payText, @"Pay Online:\s*\$?([\d,\.]+)");
                if (match.Success)
                {
                    bill.PayOnlineAmount = match.Groups[1].Value;
                    bill.Status = "Balance Due";
                }
            }

            // Check if paid in full
            var paidNode = doc.DocumentNode.SelectSingleNode($"//a[@id='taxpaid2{bill.Year}-button']//span");
            if (paidNode != null && CleanText(paidNode.InnerText).Contains("Paid in Full"))
            {
                bill.Status = "Paid in Full";
            }

            // Check payment history
            var historyNode = doc.DocumentNode.SelectSingleNode($"//a[@id='taxpaymenthistory2{bill.Year}-button']//span");
            if (historyNode != null && CleanText(historyNode.InnerText).Contains("Payment History"))
            {
                bill.Status = "Payment History Available";
            }

            bills.Add(bill);
        }

        return bills;
    }

    private List<ExemptionDetail> ParseExemptions(HtmlDocument doc)
    {
        var exemptions = new List<ExemptionDetail>();

        for (int i = 0; i < 10; i++)
        {
            var year = GetElementValue(doc, $"ContentPlaceHolder1_ExemptionInfo_rptExemptions_exemptionTaxYear_{i}");
            if (string.IsNullOrEmpty(year)) break;

            var exemption = new ExemptionDetail
            {
                Year = year.TrimEnd(':'),
                MoreInfoUrl = "http://www.cookcountyassessor.com/Exemptions/Exemption-Forms.aspx"
            };

            // Parse exemptions received count
            var exemptionNode = doc.DocumentNode.SelectSingleNode($"//a[@id='exemptionzero2{exemption.Year}-button']//span");
            if (exemptionNode != null)
            {
                var text = CleanText(exemptionNode.InnerText);
                var match = Regex.Match(text, @"(\d+)\s+Exemptions?\s+Received");
                if (match.Success)
                {
                    exemption.ExemptionsReceived = int.Parse(match.Groups[1].Value);
                }
            }

            exemptions.Add(exemption);
        }

        return exemptions;
    }

    private List<AppealDetail> ParseAppeals(HtmlDocument doc)
    {
        var appeals = new List<AppealDetail>();

        for (int i = 0; i < 10; i++)
        {
            var year = GetElementValue(doc, $"ContentPlaceHolder1_AppealsInfo_rptAppeals_appealTaxYear_{i}");
            if (string.IsNullOrEmpty(year)) break;

            var appeal = new AppealDetail
            {
                Year = year.TrimEnd(':'),
                FilingUrls = new List<string>
                {
                    "http://www.cookcountyassessor.com/Appeals/Appeal-Deadlines.aspx",
                    "https://cookcountyboardofreview.com/dates/dates-and-deadlines"
                }
            };

            // Check Not Available
            var naNode = doc.DocumentNode.SelectSingleNode($"//a[@id='appealsna2{appeal.Year}-button']//span");
            if (naNode != null && CleanText(naNode.InnerText).Contains("Not Available"))
            {
                appeal.Status = "Not Available";
                appeal.StatusMessage = "The tax billing, exemptions and/or appeals process has not started, or information not available for this tax year.";
            }

            // Check Not Accepting
            var notAcceptingNode = doc.DocumentNode.SelectSingleNode($"//a[@id='appealsnotaccepting2{appeal.Year}-button']//span");
            if (notAcceptingNode != null && CleanText(notAcceptingNode.InnerText).Contains("Appeal Information"))
            {
                appeal.Status = "Not Accepting";
                appeal.StatusMessage = "Appeals are not being accepted for this tax year.";
            }

            appeals.Add(appeal);
        }

        return appeals;
    }

    private RefundInfo? ParseRefunds(HtmlDocument doc)
    {
        var message = GetElementValue(doc, "ContentPlaceHolder1_RefundsInfo_refundMessage");
        if (string.IsNullOrEmpty(message))
            message = GetElementValue(doc, "ContentPlaceHolder1_RefundsInfo_refundMessage2");

        if (!string.IsNullOrEmpty(message))
        {
            return new RefundInfo
            {
                Status = message.Contains("No Refund") ? "No Refund Available" : "Refund Available",
                Message = message,
                MoreInfoUrl = GetLinkHref(doc, "ContentPlaceHolder1_RefundsInfo_lnkRefundsInfo")
            };
        }

        return null;
    }

    private List<TaxSaleDelinquencyDetail> ParseTaxSaleDelinquencies(HtmlDocument doc)
    {
        var delinquencies = new List<TaxSaleDelinquencyDetail>();

        for (int i = 0; i < 10; i++)
        {
            var year = GetElementValue(doc, $"ContentPlaceHolder1_RedemptionInfo_rptRedemption_Label2_{i}");
            if (string.IsNullOrEmpty(year)) break;

            var delinquency = new TaxSaleDelinquencyDetail
            {
                Year = year.TrimEnd(':'),
                MoreInfoUrl = "https://www.cookcountypropertyinfo.com/taxsale.aspx"
            };

            // Check various statuses
            var notOccurredNode = doc.DocumentNode.SelectSingleNode($"//a[@id='notaxsaleadditional2{delinquency.Year}-button']//span");
            if (notOccurredNode != null)
            {
                delinquency.Status = "Tax Sale Has Not Occurred";
                delinquency.StatusMessage = "Annual Tax Sale has not occurred.";
            }

            var noTaxSaleNode = doc.DocumentNode.SelectSingleNode($"//a[@id='notaxsale2{delinquency.Year}-button']//span");
            if (noTaxSaleNode != null)
            {
                delinquency.Status = "No Tax Sale";
                delinquency.StatusMessage = "Taxes not offered at the annual tax sale, or taxes not sold or forfeited at the annual tax sale.";
            }

            delinquencies.Add(delinquency);
        }

        return delinquencies;
    }

    private List<RecordedDocumentDetail> ParseRecordedDocuments(HtmlDocument doc)
    {
        var documents = new List<RecordedDocumentDetail>();

        // Find all document entries in the table
        var docNodes = doc.DocumentNode.SelectNodes("//div[contains(@class, 'recorddocspace')]");
        if (docNodes != null)
        {
            foreach (var node in docNodes)
            {
                var text = CleanText(node.InnerText);
                // Parse format: "1321357082 - RELEASE - 08/01/2013"
                var match = Regex.Match(text, @"(\d+)\s*-\s*(.+?)\s*-\s*([\d/]+)");
                if (match.Success)
                {
                    documents.Add(new RecordedDocumentDetail
                    {
                        DocumentNumber = match.Groups[1].Value,
                        DocumentType = match.Groups[2].Value.Trim(),
                        DateRecorded = match.Groups[3].Value,
                        MoreRecordsUrl = "https://www.cookcountyclerk.com/recordings"
                    });
                }
            }
        }

        return documents;
    }

    private PropertyImageInfo? ParsePropertyImage(HtmlDocument doc)
    {
        var imgNode = doc.DocumentNode.SelectSingleNode("//img[@id='ContentPlaceHolder1_PropertyImage_propertyImage']");
        var cookViewerNode = doc.DocumentNode.SelectSingleNode("//a[@id='ContentPlaceHolder1_PropertyImage_gisLink']");

        if (imgNode != null || cookViewerNode != null)
        {
            var streetViewUrl = imgNode?.GetAttributeValue("src", null);
            var cookViewerUrl = cookViewerNode?.GetAttributeValue("href", null);

            return new PropertyImageInfo
            {
                // Decode HTML entities like &amp; to &
                StreetViewUrl = streetViewUrl != null ? WebUtility.HtmlDecode(streetViewUrl) : null,
                CookViewerUrl = cookViewerUrl != null ? WebUtility.HtmlDecode(cookViewerUrl) : null
            };
        }

        return null;
    }

    // Helper methods
    private string? GetElementValue(HtmlDocument doc, string id)
    {
        var node = doc.DocumentNode.SelectSingleNode($"//*[@id='{id}']");
        return node != null ? CleanText(node.InnerText) : null;
    }

    private string? GetLinkHref(HtmlDocument doc, string id)
    {
        var node = doc.DocumentNode.SelectSingleNode($"//a[@id='{id}']");
        var href = node?.GetAttributeValue("href", null);
        // Decode HTML entities like &amp; to &
        return href != null ? WebUtility.HtmlDecode(href) : null;
    }

    private string CleanText(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        
        // Decode HTML entities (e.g., &nbsp; -> space, &amp; -> &)
        text = WebUtility.HtmlDecode(text);
        
        // Replace multiple whitespace characters (including non-breaking spaces) with a single space
        text = Regex.Replace(text, @"\s+", " ");
        
        // Trim leading and trailing whitespace
        text = text.Trim();
        
        return text;
    }

    private bool HasAnyValue<T>(T obj) where T : class
    {
        if (obj == null) return false;
        return obj.GetType()
            .GetProperties()
            .Any(p => p.GetValue(obj) != null && 
                     (p.PropertyType != typeof(string) || !string.IsNullOrEmpty(p.GetValue(obj) as string)));
    }
}
