using System.Net;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using CookCountyApi.Models;

namespace CookCountyApi.Services;

public interface IAssessorHtmlParser
{
    AssessorFullData ParseAssessorHtml(string html, string pin);
}

public class AssessorHtmlParser : IAssessorHtmlParser
{
    public AssessorFullData ParseAssessorHtml(string html, string pin)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var data = new AssessorFullData
        {
            Pin = pin,
            FetchedAt = DateTime.UtcNow.ToString("o")
        };

        // Parse Property Info from the "PIN & Address" section
        data.PropertyInfo = ParsePropertyInfo(doc);

        // Parse Valuation Info from "Assessed Valuation" accordion
        data.ValuationInfo = ParseValuationInfo(doc);

        // Parse Building Info from "Characteristics" accordion
        data.BuildingInfo = ParseBuildingInfo(doc);

        // Parse Land Info from "Tax Details" section
        data.LandInfo = ParseLandInfo(doc);

        // Parse Appeal History from "Appeal History" accordion
        data.AppealHistory = ParseAppealHistory(doc);

        // Parse Exemption History from "Exemption History & Status" accordion
        data.ExemptionHistory = ParseExemptionHistory(doc);

        return data;
    }

    private AssessorPropertyInfo? ParsePropertyInfo(HtmlDocument doc)
    {
        var info = new AssessorPropertyInfo();

        // Find the property-details-info section
        var detailRows = doc.DocumentNode.SelectNodes("//div[contains(@class, 'property-details-info')]//div[contains(@class, 'detail-row')]");
        
        if (detailRows != null)
        {
            foreach (var row in detailRows)
            {
                var label = row.SelectSingleNode(".//span[contains(@class, 'detail-row--label')]")?.InnerText.Trim();
                var detail = row.SelectSingleNode(".//span[contains(@class, 'detail-row--detail')]")?.InnerText.Trim();

                if (string.IsNullOrEmpty(label) || string.IsNullOrEmpty(detail)) continue;

                switch (label.ToLower())
                {
                    case "pin":
                        info.Pin = CleanText(detail);
                        break;
                    case "address":
                        info.PropertyAddress = CleanText(detail);
                        break;
                    case "city":
                        info.City = CleanText(detail);
                        break;
                    case "township":
                        // Extract just the township name (before any links)
                        var townshipText = row.SelectSingleNode(".//span[contains(@class, 'detail-row--detail')]")?.FirstChild?.InnerText.Trim();
                        info.Township = CleanText(townshipText);
                        break;
                    case "property classification":
                        info.PropertyClassification = CleanText(detail);
                        break;
                    case "square footage (land)":
                        info.LandSquareFootage = CleanText(detail);
                        break;
                    case "neighborhood":
                        info.Neighborhood = CleanText(detail);
                        break;
                    case "taxcode":
                        info.TaxCode = CleanText(detail);
                        break;
                    case "next scheduled reassessment":
                        info.NextReassessment = CleanText(detail);
                        break;
                }
            }
        }

        return info;
    }

    private AssessorValuationInfo? ParseValuationInfo(HtmlDocument doc)
    {
        var valuation = new AssessorValuationInfo();

        // Find the Assessed Valuation accordion content
        var valuationPanel = doc.DocumentNode.SelectSingleNode("//div[@id='collapseOne']");
        if (valuationPanel == null) return null;

        // Get column headers to identify years
        var headerCols = valuationPanel.SelectNodes(".//div[contains(@class, 'pt-header')]//div[starts-with(@class, 'col-')]");
        var years = new List<string>();
        
        if (headerCols != null)
        {
            foreach (var col in headerCols.Skip(1)) // Skip first empty column
            {
                var yearText = Regex.Match(col.InnerText, @"\d{4}").Value;
                if (!string.IsNullOrEmpty(yearText))
                    years.Add(yearText);
            }
        }

        // Get data rows
        var dataRows = valuationPanel.SelectNodes(".//div[contains(@class, 'pt-body')]");
        if (dataRows != null)
        {
            foreach (var row in dataRows)
            {
                var label = row.SelectSingleNode(".//div[contains(@class, 'pt-header')]")?.InnerText.Trim();
                var values = row.SelectNodes(".//div[starts-with(@class, 'col-xs-')]")?.Skip(1).Select(n => CleanText(n.InnerText)).ToList();

                if (string.IsNullOrEmpty(label) || values == null) continue;

                // Map values to properties based on label
                if (label.Contains("Total Estimated Market Value", StringComparison.OrdinalIgnoreCase))
                {
                    if (values.Count > 0) valuation.CurrentMarketValue = values[0];
                    if (values.Count > 1) valuation.PriorMarketValue = values[1];
                }
                else if (label.Contains("Total Assessed Value", StringComparison.OrdinalIgnoreCase))
                {
                    if (values.Count > 0) valuation.CurrentAssessedValue = values[0];
                    if (values.Count > 1) valuation.PriorAssessedValue = values[1];
                }
                else if (label.Contains("Land Assessed Value", StringComparison.OrdinalIgnoreCase))
                {
                    if (values.Count > 0) valuation.CurrentLandValue = values[0];
                    if (values.Count > 1) valuation.PriorLandValue = values[1];
                }
                else if (label.Contains("Building Assessed Value", StringComparison.OrdinalIgnoreCase))
                {
                    if (values.Count > 0) valuation.CurrentBuildingValue = values[0];
                    if (values.Count > 1) valuation.PriorBuildingValue = values[1];
                }
            }
        }

        return valuation;
    }

    private AssessorBuildingInfo? ParseBuildingInfo(HtmlDocument doc)
    {
        var building = new AssessorBuildingInfo();

        // Find the Characteristics accordion content
        var charPanel = doc.DocumentNode.SelectSingleNode("//div[@id='collapseTwo']");
        if (charPanel == null) return null;

        var detailRows = charPanel.SelectNodes(".//div[contains(@class, 'detail-row')]");
        if (detailRows != null)
        {
            foreach (var row in detailRows)
            {
                var label = row.SelectSingleNode(".//span[contains(@class, 'detail-row--label')]")?.InnerText.Trim();
                var detail = row.SelectSingleNode(".//span[contains(@class, 'detail-row--detail')]")?.InnerText.Trim();

                if (string.IsNullOrEmpty(label) || string.IsNullOrEmpty(detail)) continue;

                switch (label.ToLower())
                {
                    case "description":
                        building.Description = CleanText(detail);
                        break;
                    case "residence type":
                        building.ResidenceType = CleanText(detail);
                        break;
                    case "use":
                        building.Use = CleanText(detail);
                        break;
                    case "apartments":
                        building.Apartments = CleanText(detail);
                        break;
                    case "exterior construction":
                        building.ExteriorConstruction = CleanText(detail);
                        break;
                    case "full baths":
                        building.FullBaths = CleanText(detail);
                        break;
                    case "half baths":
                        building.HalfBaths = CleanText(detail);
                        break;
                    case "basement":
                        building.Basement = CleanText(detail);
                        break;
                    case "attic":
                        building.Attic = CleanText(detail);
                        break;
                    case "central air":
                        building.CentralAir = CleanText(detail);
                        break;
                    case "number of fireplaces":
                        building.Fireplaces = CleanText(detail);
                        break;
                    case "garage size/type":
                        building.GarageType = CleanText(detail);
                        break;
                    case "age":
                        building.Age = CleanText(detail);
                        break;
                    case "building square footage":
                        building.SquareFootage = CleanText(detail);
                        break;
                    case "assessment phase":
                        building.AssessmentPhase = CleanText(detail);
                        break;
                }
            }
        }

        return building;
    }

    private AssessorLandInfo? ParseLandInfo(HtmlDocument doc)
    {
        var land = new AssessorLandInfo();

        // Get land square footage from Tax Details section
        var landSqFt = doc.DocumentNode.SelectSingleNode("//span[contains(text(), 'Square Footage (Land)')]/following-sibling::span")?.InnerText.Trim();
        if (!string.IsNullOrEmpty(landSqFt))
            land.SquareFootage = CleanText(landSqFt);

        return land;
    }

    private List<AssessorAppealHistory> ParseAppealHistory(HtmlDocument doc)
    {
        var appeals = new List<AssessorAppealHistory>();

        // Find the Appeal History accordion content
        var appealPanel = doc.DocumentNode.SelectSingleNode("//div[@id='collapseFive']");
        if (appealPanel == null) return appeals;

        // Look for appeal data (structure may vary)
        var appealRows = appealPanel.SelectNodes(".//div[contains(@class, 'row')]");
        if (appealRows != null)
        {
            foreach (var row in appealRows)
            {
                var appeal = new AssessorAppealHistory
                {
                    Year = CleanText(row.SelectSingleNode(".//td[1]")?.InnerText),
                    Status = CleanText(row.SelectSingleNode(".//td[2]")?.InnerText),
                    Result = CleanText(row.SelectSingleNode(".//td[3]")?.InnerText)
                };

                if (!string.IsNullOrEmpty(appeal.Year))
                    appeals.Add(appeal);
            }
        }

        return appeals;
    }

    private List<AssessorExemptionHistory> ParseExemptionHistory(HtmlDocument doc)
    {
        var exemptions = new List<AssessorExemptionHistory>();

        // Find the Exemption History accordion content
        var exemptionPanel = doc.DocumentNode.SelectSingleNode("//div[@id='collapseFour']");
        if (exemptionPanel == null) return exemptions;

        // Get the exemption table rows
        var tableRows = exemptionPanel.SelectNodes(".//div[contains(@class, 'pt-body')]");
        if (tableRows != null)
        {
            foreach (var row in tableRows)
            {
                var year = row.SelectSingleNode(".//div[contains(@class, 'pt-header')]")?.InnerText.Trim();
                var cols = row.SelectNodes(".//div[starts-with(@class, 'col-xs-')]")?.Skip(1).ToList();

                if (string.IsNullOrEmpty(year) || cols == null) continue;

                var exemption = new AssessorExemptionHistory
                {
                    Year = year,
                    Homeowner = cols.ElementAtOrDefault(0)?.InnerText.Trim(),
                    Senior = cols.ElementAtOrDefault(1)?.InnerText.Trim(),
                    SeniorFreeze = cols.ElementAtOrDefault(2)?.InnerText.Trim(),
                    DisabledPersons = cols.ElementAtOrDefault(3)?.InnerText.Trim(),
                    DisabledVeterans = cols.ElementAtOrDefault(4)?.InnerText.Trim()
                };

                exemptions.Add(exemption);
            }
        }

        return exemptions;
    }

    private string CleanText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        text = WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"\s+", " ");
        text = Regex.Replace(text, @"<[^>]+>", ""); // Remove any HTML tags
        return text.Trim();
    }
}
