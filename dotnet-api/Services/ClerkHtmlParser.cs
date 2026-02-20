using System.Net;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using CookCountyApi.Models;

namespace CookCountyApi.Services;

public interface IClerkHtmlParser
{
    ClerkFullData ParseClerkHtml(string html, string pin);
}

public class ClerkHtmlParser : IClerkHtmlParser
{
    public ClerkFullData ParseClerkHtml(string html, string pin)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var data = new ClerkFullData
        {
            Pin = pin,
            FetchedAt = DateTime.UtcNow.ToString("o")
        };

        // Parse Data As Of date
        data.DataAsOf = ParseDataAsOf(doc);

        // Parse Sold Tax Information
        data.SoldTaxInfo = ParseSoldTaxInfo(doc);

        // Parse Delinquent Tax Information
        data.DelinquentTaxInfo = ParseDelinquentTaxInfo(doc);

        return data;
    }

    private string? ParseDataAsOf(HtmlDocument doc)
    {
        var node = doc.DocumentNode.SelectSingleNode("//text()[contains(., 'Data as of')]");
        if (node != null)
        {
            var text = CleanText(node.InnerText);
            var match = Regex.Match(text, @"Data as of[:\s]+([\d/]+)");
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }
        return null;
    }

    private ClerkSoldTaxInfo? ParseSoldTaxInfo(HtmlDocument doc)
    {
        var info = new ClerkSoldTaxInfo();

        // Parse sold tax table
        var soldTaxTable = doc.DocumentNode.SelectSingleNode("//table[contains(@class, 'soldtax') or .//th[contains(text(), 'Tax Sale')]]");
        if (soldTaxTable != null)
        {
            var rows = soldTaxTable.SelectNodes(".//tr[td]");
            if (rows != null)
            {
                foreach (var row in rows)
                {
                    var cells = row.SelectNodes(".//td");
                    if (cells != null && cells.Count >= 4)
                    {
                        var soldTax = new ClerkSoldTaxDetail
                        {
                            TaxSale = CleanText(cells[0].InnerText),
                            TaxYearRange = CleanText(cells[1].InnerText),
                            Status = CleanText(cells[2].InnerText),
                            StatusDocNumber = cells.Count > 3 ? CleanText(cells[3].InnerText) : null,
                            Date = cells.Count > 4 ? CleanText(cells[4].InnerText) : null,
                            Comment = cells.Count > 5 ? CleanText(cells[5].InnerText) : null
                        };
                        info.SoldTaxes.Add(soldTax);
                    }
                }
            }
        }

        // Parse total balances
        var totalBalance1st = doc.DocumentNode.SelectSingleNode("//*[contains(text(), 'Total Tax Balance Due 1st')]");
        if (totalBalance1st != null)
        {
            var match = Regex.Match(CleanText(totalBalance1st.InnerText), @"\$?([\d,\.]+)");
            if (match.Success)
            {
                info.TotalBalance1stInstallment = match.Groups[1].Value;
            }
        }

        var totalBalance2nd = doc.DocumentNode.SelectSingleNode("//*[contains(text(), 'Total Tax Balance Due 2nd')]");
        if (totalBalance2nd != null)
        {
            var match = Regex.Match(CleanText(totalBalance2nd.InnerText), @"\$?([\d,\.]+)");
            if (match.Success)
            {
                info.TotalBalance2ndInstallment = match.Groups[1].Value;
            }
        }

        return info.SoldTaxes.Count > 0 || !string.IsNullOrEmpty(info.TotalBalance1stInstallment) ? info : null;
    }

    private ClerkDelinquentTaxInfo? ParseDelinquentTaxInfo(HtmlDocument doc)
    {
        var info = new ClerkDelinquentTaxInfo();

        // Parse delinquent tax table
        var delinquentTable = doc.DocumentNode.SelectSingleNode("//table[contains(@class, 'delinquent') or .//th[contains(text(), 'Tax Year')]]");
        if (delinquentTable != null)
        {
            var rows = delinquentTable.SelectNodes(".//tr[td]");
            if (rows != null)
            {
                foreach (var row in rows)
                {
                    var cells = row.SelectNodes(".//td");
                    if (cells != null && cells.Count >= 3)
                    {
                        var delinquentTax = new ClerkDelinquentTaxDetail
                        {
                            TaxYear = CleanText(cells[0].InnerText),
                            Status = CleanText(cells[1].InnerText),
                            ForfeitDate = cells.Count > 2 ? CleanText(cells[2].InnerText) : null,
                            FirstInstallmentBalance = cells.Count > 3 ? CleanText(cells[3].InnerText) : null,
                            SecondInstallmentBalance = cells.Count > 4 ? CleanText(cells[4].InnerText) : null,
                            Type = cells.Count > 5 ? CleanText(cells[5].InnerText) : null,
                            WarrantYear = cells.Count > 6 ? CleanText(cells[6].InnerText) : null
                        };
                        info.DelinquentTaxes.Add(delinquentTax);
                    }
                }
            }
        }

        return info.DelinquentTaxes.Count > 0 ? info : null;
    }

    private string CleanText(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        text = WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"\s+", " ");
        return text.Trim();
    }
}
