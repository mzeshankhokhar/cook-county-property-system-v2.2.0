using System.Net;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using CookCountyApi.Models;

namespace CookCountyApi.Services;

public interface IRecorderHtmlParser
{
    RecorderFullData ParseRecorderHtml(string html, string pin);
}

public class RecorderHtmlParser : IRecorderHtmlParser
{
    public RecorderFullData ParseRecorderHtml(string html, string pin)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var data = new RecorderFullData
        {
            Pin = pin,
            FetchedAt = DateTime.UtcNow.ToString("o")
        };

        // Parse Property Info from fieldset (if present in ResultByPin format)
        data.PropertyInfo = ParsePropertyInfo(doc);

        // Parse Documents from table
        data.Documents = ParseDocuments(doc);
        data.TotalDocuments = data.Documents.Count;

        return data;
    }

    private RecorderPropertyInfo? ParsePropertyInfo(HtmlDocument doc)
    {
        var info = new RecorderPropertyInfo();

        // Find the fieldset with PIN & Address (only in ResultByPin format)
        var fieldset = doc.DocumentNode.SelectSingleNode("//fieldset[legend[contains(text(), 'PIN & Address')] or legend[contains(text(), 'PIN &amp; Address')]]");
        
        if (fieldset != null)
        {
            // The structure is: div.row > div.col-md-1 (label) + div.col-md-4 (value)
            // Get all columns and process them in pairs
            var allColumns = fieldset.SelectNodes(".//div[starts-with(@class, 'col-md-')]");
            
            if (allColumns != null)
            {
                for (int i = 0; i < allColumns.Count - 1; i++)
                {
                    var currentCol = allColumns[i];
                    var label = currentCol.SelectSingleNode(".//label")?.InnerText?.Trim();
                    
                    if (!string.IsNullOrEmpty(label))
                    {
                        // Next column should have the value
                        var nextCol = allColumns[i + 1];
                        var value = nextCol.SelectSingleNode(".//span")?.InnerText?.Trim();
                        
                        if (!string.IsNullOrEmpty(value))
                        {
                            switch (label.ToLower())
                            {
                                case "address:":
                                    info.Address = CleanText(value);
                                    break;
                                case "city:":
                                    info.City = CleanText(value);
                                    break;
                                case "zipcode:":
                                    info.Zipcode = CleanText(value);
                                    break;
                            }
                        }
                        
                        // Skip the value column in next iteration
                        i++;
                    }
                }
            }
        }

        return !string.IsNullOrEmpty(info.Address) ? info : null;
    }

    private List<RecorderDocumentFull> ParseDocuments(HtmlDocument doc)
    {
        var documents = new List<RecorderDocumentFull>();

        // Find the main data table
        var table = doc.DocumentNode.SelectSingleNode("//table[contains(@class, 'table')]");
        if (table == null) return documents;

        // Get table headers to determine format
        var headers = table.SelectNodes(".//thead//th");
        if (headers == null) return documents;

        // Detect format by checking headers
        bool isAddressSearchFormat = headers.Any(h => h.InnerText.Contains("PIN", StringComparison.OrdinalIgnoreCase) && 
                                                      h.InnerText.Contains("Address", StringComparison.OrdinalIgnoreCase));
        
        // For AddressSearch format, we don't parse documents (it just shows summary)
        if (isAddressSearchFormat)
        {
            return documents;
        }

        // Get all data rows (skip header)
        var rows = table.SelectNodes(".//tbody/tr");
        if (rows == null) return documents;

        // Detect if this is extended format (has Grantor/Grantee columns)
        // Extended format has 10+ columns, simple ResultByPin has 6 columns
        var hasExtendedColumns = headers.Count >= 9;

        foreach (var row in rows)
        {
            try
            {
                var cells = row.SelectNodes(".//td");
                if (cells == null || cells.Count < 5) continue;

                var document = new RecorderDocumentFull();

                if (hasExtendedColumns && cells.Count >= 10)
                {
                    // Extended format: Checkbox | View | DocNum | Recorded | Executed | Type | Grantor | Grantee | AssocDoc | PIN
                    ParseExtendedFormat(cells, document);
                }
                else
                {
                    // Simple ResultByPin format: Checkbox | View | DocNum | Recorded | Executed | Type
                    ParseSimpleFormat(cells, document);
                }

                // Only add if we have at least document number and type
                if (!string.IsNullOrEmpty(document.DocumentNumber) && !string.IsNullOrEmpty(document.DocumentType))
                {
                    documents.Add(document);
                }
            }
            catch
            {
                // Skip malformed rows
                continue;
            }
        }

        return documents;
    }

    private void ParseSimpleFormat(HtmlNodeCollection cells, RecorderDocumentFull document)
    {
        // Cell 0: Checkbox (skip)
        // Cell 1: View link
        var viewLink = cells[1].SelectSingleNode(".//a[@href]");
        if (viewLink != null)
        {
            var url = viewLink.GetAttributeValue("href", null);
            if (url != null)
            {
                // Decode HTML entities like &amp; to &
                url = WebUtility.HtmlDecode(url);
                
                // Make it a complete URL if it's relative
                if (!url.StartsWith("http"))
                {
                    url = "https://crs.cookcountyclerkil.gov" + (url.StartsWith("/") ? url : "/" + url);
                }
                
                document.ViewUrl = url;
            }
        }

        // Cell 2: Document Number
        var docNumSpan = cells[2].SelectSingleNode(".//span");
        if (docNumSpan != null)
        {
            document.DocumentNumber = CleanText(docNumSpan.InnerText);
        }

        // Cell 3: Date Recorded
        var dateRecordedSpan = cells[3].SelectSingleNode(".//span");
        if (dateRecordedSpan != null)
        {
            document.DateRecorded = CleanText(dateRecordedSpan.InnerText);
        }

        // Cell 4: Date Executed
        var dateExecutedSpan = cells[4].SelectSingleNode(".//span");
        if (dateExecutedSpan != null)
        {
            document.DateExecuted = CleanText(dateExecutedSpan.InnerText);
        }

        // Cell 5: Document Type
        var docTypeSpan = cells[5].SelectSingleNode(".//span");
        if (docTypeSpan != null)
        {
            document.DocumentType = CleanText(docTypeSpan.InnerText);
        }
    }

    private void ParseExtendedFormat(HtmlNodeCollection cells, RecorderDocumentFull document)
    {
        // Cell 0: Checkbox (skip)
        // Cell 1: View link
        var viewLink = cells[1].SelectSingleNode(".//a[@href]");
        if (viewLink != null)
        {
            var url = viewLink.GetAttributeValue("href", null);
            if (url != null)
            {
                // Decode HTML entities like &amp; to &
                url = WebUtility.HtmlDecode(url);
                
                // Make it a complete URL if it's relative
                if (!url.StartsWith("http"))
                {
                    url = "https://crs.cookcountyclerkil.gov" + (url.StartsWith("/") ? url : "/" + url);
                }
                
                document.ViewUrl = url;
            }
        }

        // Cell 2: Document Number
        var docNumSpan = cells[2].SelectSingleNode(".//span");
        if (docNumSpan != null)
        {
            document.DocumentNumber = CleanText(docNumSpan.InnerText);
        }

        // Cell 3: Date Recorded
        var dateRecordedSpan = cells[3].SelectSingleNode(".//span");
        if (dateRecordedSpan != null)
        {
            document.DateRecorded = CleanText(dateRecordedSpan.InnerText);
        }

        // Cell 4: Date Executed
        var dateExecutedSpan = cells[4].SelectSingleNode(".//span");
        if (dateExecutedSpan != null)
        {
            document.DateExecuted = CleanText(dateExecutedSpan.InnerText);
        }

        // Cell 5: Document Type
        var docTypeSpan = cells[5].SelectSingleNode(".//span");
        if (docTypeSpan != null)
        {
            document.DocumentType = CleanText(docTypeSpan.InnerText);
        }

        // Cell 6: First Grantor
        var grantorLink = cells[6].SelectSingleNode(".//a");
        if (grantorLink != null)
        {
            document.FirstGrantor = CleanText(grantorLink.InnerText);
        }

        // Cell 7: First Grantee
        var granteeLink = cells[7].SelectSingleNode(".//a");
        if (granteeLink != null)
        {
            document.FirstGrantee = CleanText(granteeLink.InnerText);
        }

        // Cell 8: Associated Doc Number
        var assocDocLink = cells[8].SelectSingleNode(".//a");
        if (assocDocLink != null)
        {
            document.AssociatedDocNumber = CleanText(assocDocLink.InnerText);
        }

        // Cell 9: First PIN with Address
        var pinLink = cells[9].SelectSingleNode(".//a");
        if (pinLink != null)
        {
            document.FirstPIN = CleanText(pinLink.InnerText);
        }
        
        // Also get the address (in small span below PIN)
        var addressSpan = cells[9].SelectSingleNode(".//span[@class='small']//span");
        if (addressSpan != null)
        {
            document.PropertyAddress = CleanText(addressSpan.InnerText);
        }
    }

    private string CleanText(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        text = WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"\s+", " ");
        return text.Trim();
    }
}
