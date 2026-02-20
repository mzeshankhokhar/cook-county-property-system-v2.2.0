using System.Net;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using CookCountyApi.Models;

namespace CookCountyApi.Services;

public interface ITreasurerHtmlParser
{
    TreasurerFullData ParseTreasurerHtml(string html, string pin);
}

public class TreasurerHtmlParser : ITreasurerHtmlParser
{
    public TreasurerFullData ParseTreasurerHtml(string html, string pin)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var data = new TreasurerFullData
        {
            Pin = pin,
            FetchedAt = DateTime.UtcNow.ToString("o")
        };

        // Check for redirect URL
        var metaRefresh = doc.DocumentNode.SelectSingleNode("//meta[@http-equiv='refresh']");
        if (metaRefresh != null)
        {
            var content = metaRefresh.GetAttributeValue("content", "");
            var match = Regex.Match(content, @"url=(.+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                data.RedirectUrl = match.Groups[1].Value;
            }
        }

        // Parse Property Info
        data.PropertyInfo = ParsePropertyInfo(doc);

        // Parse Tax Bills
        data.TaxBills = ParseTaxBills(doc);

        // Parse Payment Summary
        data.PaymentSummary = ParsePaymentSummary(doc);

        return data;
    }

    private TreasurerPropertyInfo? ParsePropertyInfo(HtmlDocument doc)
    {
        var info = new TreasurerPropertyInfo();

        // Property Address from the "Property Location" section
        var addressNode = doc.DocumentNode.SelectSingleNode("//span[@id='ContentPlaceHolder1_OverviewDataResultsSummary1_PropertyLocationAndMailingAddressControl1_lblPropertyAddress']");
        var cityStateZipNode = doc.DocumentNode.SelectSingleNode("//span[@id='ContentPlaceHolder1_OverviewDataResultsSummary1_PropertyLocationAndMailingAddressControl1_lblPropertyCityStateZipCode']");
        
        if (addressNode != null)
        {
            var address = CleanText(addressNode.InnerText);
            var cityStateZip = cityStateZipNode != null ? CleanText(cityStateZipNode.InnerText) : "";
            info.Address = $"{address}, {cityStateZip}".Trim(' ', ',').Replace(",", ", ");
        }

        // Owner/Taxpayer Name from "Mailing Information" section
        var taxpayerNameNode = doc.DocumentNode.SelectSingleNode("//span[@id='ContentPlaceHolder1_OverviewDataResultsSummary1_PropertyLocationAndMailingAddressControl1_lblTaxpayerName1']");
        if (taxpayerNameNode != null)
        {
            info.Owner = CleanText(taxpayerNameNode.InnerText);
            info.TaxpayerName = info.Owner;
        }

        // Volume
        var volumeNode = doc.DocumentNode.SelectSingleNode("//span[@id='ContentPlaceHolder1_OverviewDataResultsSummary1_PropertyLocationAndMailingAddressControl1_lblVolume']");
        if (volumeNode != null)
        {
            info.Volume = CleanText(volumeNode.InnerText);
        }

        return HasAnyValue(info) ? info : null;
    }

    private List<TreasurerTaxBill> ParseTaxBills(HtmlDocument doc)
    {
        var bills = new List<TreasurerTaxBill>();

        // Parse Tax Year 2023 (Special Collection)
        var taxYear2023Node = doc.DocumentNode.SelectSingleNode("//span[@id='ContentPlaceHolder1_OverviewDataResultsSummary1_PaymentsOverviewSummaryControl1_lblSpecialCollectionTaxYear']");
        var totalBilled2023Node = doc.DocumentNode.SelectSingleNode("//span[@id='ContentPlaceHolder1_OverviewDataResultsSummary1_PaymentsOverviewSummaryControl1_lblSpecialCollectionTotalAmountBilled']");
        var totalDue2023Node = doc.DocumentNode.SelectSingleNode("//span[@id='ContentPlaceHolder1_OverviewDataResultsSummary1_PaymentsOverviewSummaryControl1_lblSpecialCollectionTotalAmountDue']");

        if (taxYear2023Node != null && totalBilled2023Node != null)
        {
            var taxYear = ExtractTaxYear(CleanText(taxYear2023Node.InnerText));
            bills.Add(new TreasurerTaxBill
            {
                TaxYear = taxYear,
                TotalAmount = CleanText(totalBilled2023Node.InnerText),
                AmountDue = totalDue2023Node != null ? CleanText(totalDue2023Node.InnerText) : null,
                Status = DetermineStatus(totalDue2023Node != null ? CleanText(totalDue2023Node.InnerText) : "$0.00")
            });
        }

        // Parse Tax Year 2024 (Prior Year)
        var taxYear2024Node = doc.DocumentNode.SelectSingleNode("//span[@id='ContentPlaceHolder1_OverviewDataResultsSummary1_PaymentsOverviewSummaryControl1_lblPriorTaxYear']");
        var totalBilled2024Node = doc.DocumentNode.SelectSingleNode("//span[@id='ContentPlaceHolder1_OverviewDataResultsSummary1_PaymentsOverviewSummaryControl1_lblPriorTotalAmountBilled']");
        var totalDue2024Node = doc.DocumentNode.SelectSingleNode("//span[@id='ContentPlaceHolder1_OverviewDataResultsSummary1_PaymentsOverviewSummaryControl1_lblPriorTotalAmountDue']");

        if (taxYear2024Node != null && totalBilled2024Node != null)
        {
            var taxYear = ExtractTaxYear(CleanText(taxYear2024Node.InnerText));
            bills.Add(new TreasurerTaxBill
            {
                TaxYear = taxYear,
                TotalAmount = CleanText(totalBilled2024Node.InnerText),
                AmountDue = totalDue2024Node != null ? CleanText(totalDue2024Node.InnerText) : null,
                Status = DetermineStatus(totalDue2024Node != null ? CleanText(totalDue2024Node.InnerText) : "$0.00")
            });
        }

        // Parse installment details
        ParseInstallmentDetails(doc, bills);

        return bills;
    }

    private void ParseInstallmentDetails(HtmlDocument doc, List<TreasurerTaxBill> bills)
    {
        // Look for payment boxes with installment information
        var installment1Nodes = doc.DocumentNode.SelectNodes("//div[contains(@class, 'paymentstatusinstallment1')]");
        var installment2Nodes = doc.DocumentNode.SelectNodes("//div[contains(@class, 'paymentstatusinstallment2')]");

        if (installment1Nodes != null && installment1Nodes.Count > 0)
        {
            foreach (var node in installment1Nodes)
            {
                // Extract installment data (you can enhance this further)
                var billAmount = node.SelectSingleNode(".//div[contains(text(), 'Original Billed Amount:')]/following-sibling::div");
                var dueDate = node.SelectSingleNode(".//div[contains(text(), 'Due Date:')]/following-sibling::div");
                var amountDue = node.SelectSingleNode(".//div[contains(text(), 'Current Amount Due:')]/following-sibling::div");

                if (billAmount != null)
                {
                    // You can add installment details to the bills if needed
                    // For now, we're keeping it simple
                }
            }
        }
    }

    private TreasurerPaymentSummary? ParsePaymentSummary(HtmlDocument doc)
    {
        var info = new TreasurerPaymentSummary();

        // Total Taxing District Debt
        var debtNode = doc.DocumentNode.SelectSingleNode("//span[@id='ContentPlaceHolder1_DebtToPropertyValuePercentTotalControl1_lblTotalTaxingAgencyDebtDesktop']");
        if (debtNode != null)
        {
            info.TotalDebt = CleanText(debtNode.InnerText);
        }

        // Property Value
        var propertyValueNode = doc.DocumentNode.SelectSingleNode("//span[@id='ContentPlaceHolder1_DebtToPropertyValuePercentTotalControl1_lblPropertyValueDesktop']");
        if (propertyValueNode != null)
        {
            info.PropertyValue = CleanText(propertyValueNode.InnerText);
        }

        // Debt Percentage
        var debtPercentNode = doc.DocumentNode.SelectSingleNode("//span[@id='ContentPlaceHolder1_DebtToPropertyValuePercentTotalControl1_lblTotalDebtPercentDesktop']");
        if (debtPercentNode != null)
        {
            info.DebtPercent = CleanText(debtPercentNode.InnerText);
        }

        // Calculate total billed and total due from tax bills
        var totalBilled2023 = doc.DocumentNode.SelectSingleNode("//span[@id='ContentPlaceHolder1_OverviewDataResultsSummary1_PaymentsOverviewSummaryControl1_lblSpecialCollectionTotalAmountBilled']");
        var totalBilled2024 = doc.DocumentNode.SelectSingleNode("//span[@id='ContentPlaceHolder1_OverviewDataResultsSummary1_PaymentsOverviewSummaryControl1_lblPriorTotalAmountBilled']");
        var totalDue2023 = doc.DocumentNode.SelectSingleNode("//span[@id='ContentPlaceHolder1_OverviewDataResultsSummary1_PaymentsOverviewSummaryControl1_lblSpecialCollectionTotalAmountDue']");
        var totalDue2024 = doc.DocumentNode.SelectSingleNode("//span[@id='ContentPlaceHolder1_OverviewDataResultsSummary1_PaymentsOverviewSummaryControl1_lblPriorTotalAmountDue']");

        if (totalBilled2023 != null || totalBilled2024 != null)
        {
            decimal total = 0;
            if (totalBilled2023 != null) total += ParseCurrency(CleanText(totalBilled2023.InnerText));
            if (totalBilled2024 != null) total += ParseCurrency(CleanText(totalBilled2024.InnerText));
            info.TotalBilled = $"${total:N2}";
        }

        if (totalDue2023 != null || totalDue2024 != null)
        {
            decimal total = 0;
            if (totalDue2023 != null) total += ParseCurrency(CleanText(totalDue2023.InnerText));
            if (totalDue2024 != null) total += ParseCurrency(CleanText(totalDue2024.InnerText));
            info.TotalDue = $"${total:N2}";
        }

        return HasAnyValue(info) ? info : null;
    }

    private string ExtractTaxYear(string text)
    {
        // Extract "2023" from "Tax Year 2023 (billed in 2024)"
        var match = Regex.Match(text, @"Tax Year (\d{4})");
        return match.Success ? match.Groups[1].Value : text;
    }

    private string DetermineStatus(string amountDue)
    {
        var amount = ParseCurrency(amountDue);
        return amount == 0 ? "Paid in Full" : "Balance Due";
    }

    private decimal ParseCurrency(string value)
    {
        if (string.IsNullOrEmpty(value)) return 0;
        var cleaned = Regex.Replace(value, @"[^\d\.]", "");
        return decimal.TryParse(cleaned, out var result) ? result : 0;
    }

    private string? GetElementValue(HtmlDocument doc, string id)
    {
        var node = doc.DocumentNode.SelectSingleNode($"//*[@id='{id}' or contains(@id, '{id}')]");
        return node != null ? CleanText(node.InnerText) : null;
    }

    private string CleanText(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        text = WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"\s+", " ");
        text = Regex.Replace(text, @"&nbsp;", " ");
        return text.Trim();
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
