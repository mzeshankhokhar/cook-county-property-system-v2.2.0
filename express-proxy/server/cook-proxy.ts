import NodeCache from "node-cache";
import * as cheerio from "cheerio";

const cache = new NodeCache({ stdTTL: 300 }); // 5 minute cache

const HEADERS = {
  "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/115.0.0.0 Safari/537.36",
  "Accept": "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8",
  "Accept-Language": "en-US,en;q=0.5",
};

interface ApiResult<T> {
  success: true;
  data: T;
}

interface ApiError {
  success: false;
  error: string;
  code: string;
  statusCode: number;
}

type ApiResponse<T> = ApiResult<T> | ApiError;

function validatePin(pin: string): boolean {
  return /^\d{2}-\d{2}-\d{3}-\d{3}-\d{4}$/.test(pin);
}

function error(message: string, code: string, statusCode: number = 400): ApiError {
  return { success: false, error: message, code, statusCode };
}

export async function fetchTaxDelinquent(
  pin: string
): Promise<ApiResponse<{ html: string; pin: string; fetchedAt: string }>> {
  if (!pin || !validatePin(pin)) {
    return error("Invalid or missing PIN. Format: XX-XX-XXX-XXX-XXXX", "INVALID_PIN", 400);
  }

  // Check cache first
  const cacheKey = `tax_${pin}`;
  const cached = cache.get<string>(cacheKey);
  if (cached) {
    return {
      success: true,
      data: {
        html: cached,
        pin,
        fetchedAt: new Date().toISOString(),
      },
    };
  }

  try {
    const baseUrl = "https://taxdelinquent.cookcountyclerkil.gov/";

    // First, get the page to extract the verification token
    const initialResponse = await fetch(baseUrl, { headers: HEADERS });
    const initialHtml = await initialResponse.text();

    // Extract the token using regex
    const tokenMatch = initialHtml.match(
      /name="__RequestVerificationToken" type="hidden" value="([^"]+)"/
    );
    const token = tokenMatch?.[1];

    if (!token) {
      return error("Failed to extract verification token", "PARSE_ERROR", 502);
    }

    // Get cookies from initial request
    const cookies = initialResponse.headers.get("set-cookie") || "";

    // Prepare POST data
    const formData = new URLSearchParams();
    formData.append("__RequestVerificationToken", token);
    formData.append("Pin", pin);

    // Submit the form
    const response = await fetch(baseUrl, {
      method: "POST",
      headers: {
        ...HEADERS,
        "Content-Type": "application/x-www-form-urlencoded",
        Cookie: cookies,
      },
      body: formData.toString(),
    });

    let html = await response.text();

    // Fix relative URLs
    html = html.replace(
      /(href|src)="(\/[^"]+)"/gi,
      `$1="${baseUrl}$2"`
    );

    // Remove header section
    html = html.replace(/<header class="navbar[^>]*>[\s\S]*?<\/header>/gi, "");

    // Remove the collapseOne div
    html = html.replace(/<div id="collapseOne"[^>]*>[\s\S]*?<\/div>/gi, "");

    // Remove the search form
    html = html.replace(
      /<form[^>]*action="\/"[^>]*>[\s\S]*?<\/form>/gi,
      ""
    );

    // Add inline style to body tag
    html = html.replace(
      /<body([^>]*)>/i,
      '<body$1 style="padding-top: 0 !important;">'
    );

    // Cache the result
    cache.set(cacheKey, html);

    return {
      success: true,
      data: {
        html,
        pin,
        fetchedAt: new Date().toISOString(),
      },
    };
  } catch (err) {
    console.error("Tax delinquent fetch error:", err);
    return error("Failed to fetch tax delinquent data", "FETCH_ERROR", 502);
  }
}

export async function fetchPropertyInfo(
  pin: string
): Promise<ApiResponse<{ html: string; pin: string; fetchedAt: string }>> {
  if (!pin || !validatePin(pin)) {
    return error("Invalid or missing PIN. Format: XX-XX-XXX-XXX-XXXX", "INVALID_PIN", 400);
  }

  // Check cache first
  const cacheKey = `property_${pin}`;
  const cached = cache.get<string>(cacheKey);
  if (cached) {
    return {
      success: true,
      data: {
        html: cached,
        pin,
        fetchedAt: new Date().toISOString(),
      },
    };
  }

  try {
    // Split the PIN into its components
    const [p1, p2, p3, p4, p5] = pin.split("-");

    const baseUrl = "https://www.cookcountypropertyinfo.com/";
    const searchUrl = `${baseUrl}default.aspx`;
    const resultsUrl = `${baseUrl}pinresults.aspx`;

    // Step 1: Load the search page to get hidden fields
    const searchResponse = await fetch(searchUrl, { headers: HEADERS });
    const searchHtml = await searchResponse.text();
    const cookies = searchResponse.headers.get("set-cookie") || "";

    // Extract hidden fields using cheerio
    const $ = cheerio.load(searchHtml);
    const viewState = $('input[name="__VIEWSTATE"]').val() || "";
    const eventValidation = $('input[name="__EVENTVALIDATION"]').val() || "";
    const viewStateGen = $('input[name="__VIEWSTATEGENERATOR"]').val() || "";
    const previousPage = $('input[name="__PREVIOUSPAGE"]').val() || "";

    // Step 2: Prepare POST data
    const formData = new URLSearchParams();
    formData.append("__LASTFOCUS", "");
    formData.append("__EVENTTARGET", "");
    formData.append("__EVENTARGUMENT", "btnPIN");
    formData.append("__VIEWSTATE", String(viewState));
    formData.append("__VIEWSTATEGENERATOR", String(viewStateGen));
    formData.append("__PREVIOUSPAGE", String(previousPage));
    formData.append("__EVENTVALIDATION", String(eventValidation));
    formData.append("ctl00$PINAddressSearch2$pin2Box1", "");
    formData.append("ctl00$PINAddressSearch2$pin2Box2", "");
    formData.append("ctl00$PINAddressSearch2$pin2Box3", "");
    formData.append("ctl00$PINAddressSearch2$pin2Box4", "");
    formData.append("ctl00$PINAddressSearch2$pin2Box5", "");
    formData.append("ctl00$HiddenField1", "");
    formData.append("ctl00$ContentPlaceHolder1$PINAddressSearch$searchToValidate", "PIN");
    formData.append("ctl00$ContentPlaceHolder1$PINAddressSearch$pinBox1", p1);
    formData.append("ctl00$ContentPlaceHolder1$PINAddressSearch$pinBox2", p2);
    formData.append("ctl00$ContentPlaceHolder1$PINAddressSearch$pinBox3", p3);
    formData.append("ctl00$ContentPlaceHolder1$PINAddressSearch$pinBox4", p4);
    formData.append("ctl00$ContentPlaceHolder1$PINAddressSearch$pinBox5", p5);
    formData.append("ctl00$ContentPlaceHolder1$PINAddressSearch$btnSearch", "SEARCH");
    formData.append("g-recaptcha-response", "");
    formData.append("action", "validate_captcha");

    // Step 3: Submit the form to get results
    const resultsResponse = await fetch(resultsUrl, {
      method: "POST",
      headers: {
        ...HEADERS,
        "Content-Type": "application/x-www-form-urlencoded",
        Cookie: cookies,
      },
      body: formData.toString(),
    });

    let html = await resultsResponse.text();

    // Fix relative URLs
    html = html.replace(
      /(href|src)="(Content\/[^"]+)"/gi,
      `$1="https://www.cookcountypropertyinfo.com/$2"`
    );

    html = html.replace(
      /(href|src)="\.\.\/([^"]+)"/gi,
      `$1="https://www.cookcountypropertyinfo.com/$2"`
    );

    // Remove header and footer
    html = html.replace(/<header[^>]*>[\s\S]*?<\/header>/gi, "");
    html = html.replace(/<footer[^>]*>[\s\S]*?<\/footer>/gi, "");

    // Cache the result
    cache.set(cacheKey, html);

    return {
      success: true,
      data: {
        html,
        pin,
        fetchedAt: new Date().toISOString(),
      },
    };
  } catch (err) {
    console.error("Property info fetch error:", err);
    return error("Failed to fetch property information", "FETCH_ERROR", 502);
  }
}

export async function fetchDocumentSearch(
  pin: string
): Promise<
  ApiResponse<{
    redirectUrl: string;
    documentId?: string;
    historyId?: string;
    pin: string;
    fetchedAt: string;
  }>
> {
  if (!pin || !validatePin(pin)) {
    return error("Invalid or missing PIN. Format: XX-XX-XXX-XXX-XXXX", "INVALID_PIN", 400);
  }

  // Check cache first
  const cacheKey = `doc_${pin}`;
  const cached = cache.get<{
    redirectUrl: string;
    documentId?: string;
    historyId?: string;
  }>(cacheKey);
  if (cached) {
    return {
      success: true,
      data: {
        ...cached,
        pin,
        fetchedAt: new Date().toISOString(),
      },
    };
  }

  try {
    // Clean the PIN (remove dashes and spaces)
    const cleanPin = pin.replace(/[-\s]/g, "");
    const searchUrl = `https://crs.cookcountyclerkil.gov/Search/ResultByPin?id1=${cleanPin}`;

    // Fetch the search results page
    const response = await fetch(searchUrl, {
      headers: HEADERS,
      redirect: "follow",
    });

    const html = await response.text();

    if (!html) {
      return error("Failed to fetch search results", "FETCH_ERROR", 502);
    }

    // Parse HTML using cheerio
    const $ = cheerio.load(html);

    // Find the first link in the results table
    const linkElement = $(
      'table.tblfmt tbody tr:first-child a[href*="/Document/Detail"]'
    ).first();

    if (!linkElement.length) {
      return error("Document link not found for this PIN", "NOT_FOUND", 404);
    }

    const href = linkElement.attr("href") || "";

    // Extract dId and hId from the href
    const urlParams = new URLSearchParams(href.split("?")[1] || "");
    const dId = urlParams.get("dId") || undefined;
    const hId = urlParams.get("hId") || undefined;

    if (!dId || !hId) {
      return error("Failed to extract document identifiers", "PARSE_ERROR", 502);
    }

    const redirectUrl = `https://crs.cookcountyclerkil.gov/Document/Detail?dId=${dId}&hId=${hId}`;

    // Cache the result
    const cacheData = { redirectUrl, documentId: dId, historyId: hId };
    cache.set(cacheKey, cacheData);

    return {
      success: true,
      data: {
        redirectUrl,
        documentId: dId,
        historyId: hId,
        pin,
        fetchedAt: new Date().toISOString(),
      },
    };
  } catch (err) {
    console.error("Document search error:", err);
    return error("Failed to search for documents", "FETCH_ERROR", 502);
  }
}

// Clear cache for a specific PIN or all
export function clearCache(pin?: string): void {
  if (pin) {
    cache.del(`tax_${pin}`);
    cache.del(`property_${pin}`);
    cache.del(`doc_${pin}`);
  } else {
    cache.flushAll();
  }
}
