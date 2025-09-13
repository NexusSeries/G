using System;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using HtmlAgilityPack;
using PhoneNumbers;

public class ProfileData
{
    public string Name { get; set; } = "";
    public List<string> Phones { get; set; } = new List<string>();
    public List<string> Emails { get; set; } = new List<string>();
    public List<string> Websites { get; set; } = new List<string>();
    public string WorkInfo { get; set; } = "";
    public string EducationInfo { get; set; } = "";
    public string AboutInfo { get; set; } = "";
    public string Birthday { get; set; } = "";
    public string Relationship { get; set; } = "";
    public string Location { get; set; } = "";
    public bool IsPrivate { get; set; } = false;
}

class Program
    {
        // Important: It's recommended to handle sensitive data like tokens securely, not hardcoded.
        static string defaultNumericUserId = "100041584152497"; // 100063495199188
        static string accessToken = "EAAAAUaZA8jlABPdsHTHa2zZCDF7g5FZAyZA41iOJKHwVhxuBMqmKndcMxcD499XreUBLw681vhYL9aldQjDtDMtyOQJ2W3HtJVKxcN0py8ZAt4wryTRzPuDrs8MhJU92BmaJCNWyPmaTaHSm7OUK4r5KZC0n8SGEPofLmHXSJ2tts0W15iTJsTvguA4pg6o9zs5QZDZD";
        static string cookieString = "xs=14%3An-w5nKwjxlVg3w%3A2%3A1757675093%3A-1%3A-1%3A%3AAcU64wB_I3fsFdYh_SvgIGRWZIIMwe5ue3yj8yysKw; datr=oJTsZlxnJhWUFhq6exd8Ez06; fr=175Km7qXkQra7Z9nG.AWcOT0axaQtjOf-QuCI-K0O02caNH_txjuDvwdRmV4jMS7qGiPs.BoxAkz..AAA.0.0.BoxAo-.AWdMAzUe1CzxEYOExd9mO8b6480";

    static PhoneNumberUtil phoneUtil = PhoneNumberUtil.GetInstance();
    static ProfileData profileData = new ProfileData();

    static async Task Main(string[] args)
    {
        Console.WriteLine("馃殌 Starting Facebook profile extractor...");
        
        // Check for required environment variables
        if (string.IsNullOrEmpty(accessToken))
        {
            Console.WriteLine("鈿狅笍 FB_ACCESS_TOKEN environment variable not set. Graph API features disabled.");
        }
        if (string.IsNullOrEmpty(cookieString))
        {
            Console.WriteLine("鈿狅笍 FB_COOKIE_STRING environment variable not set. Authentication may fail.");
        }
        
        string userId = args.Length > 0 ? args[0] : defaultNumericUserId;
        Console.WriteLine($"馃幆 Extracting profile for UserID: {userId}");
        if (!await WeakMethod(userId))
        {
            Console.WriteLine("鈿狅笍 WeakMethod failed, switching to StrongMethod...");
            await StrongMethod(userId);
        }
        // Final check if profile is truly private based on all extracted data
        CheckFinalPrivacyStatus();
        
        await GenerateDynamicOutput();
        Console.WriteLine("\n鉁� Extraction complete! Check data.txt for results.");
    }

    // Weak Method: Basic scraping (fallback method)
    static async Task<bool> WeakMethod(string userId)
    {
        try
        {
            Console.WriteLine("馃暤锔� [WEAK METHOD] Scraping Facebook profile using basic mobile endpoints...");
            string url = $"https://mbasic.facebook.com/profile.php?id={userId}";
            using (HttpClient client = CreateConfiguredHttpClient())
            {
                var response = await client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    // Ensure proper decompression and encoding handling
                    byte[] contentBytes = await response.Content.ReadAsByteArrayAsync();
                    string html;
                    
                    // Check content encoding and handle decompression if needed
                    var contentEncoding = response.Content.Headers.ContentEncoding;
                    if (contentEncoding.Contains("gzip"))
                    {
                        using (var gzipStream = new System.IO.Compression.GZipStream(new MemoryStream(contentBytes), System.IO.Compression.CompressionMode.Decompress))
                        using (var reader = new StreamReader(gzipStream, System.Text.Encoding.UTF8))
                        {
                            html = await reader.ReadToEndAsync();
                        }
                    }
                    else if (contentEncoding.Contains("deflate"))
                    {
                        using (var deflateStream = new System.IO.Compression.DeflateStream(new MemoryStream(contentBytes), System.IO.Compression.CompressionMode.Decompress))
                        using (var reader = new StreamReader(deflateStream, System.Text.Encoding.UTF8))
                        {
                            html = await reader.ReadToEndAsync();
                        }
                    }
                    else
                    {
                        // No compression or unsupported compression
                        html = System.Text.Encoding.UTF8.GetString(contentBytes);
                    }
                    
                    Console.WriteLine($"鉁� [WEAK METHOD] Successfully fetched profile page with {html.Length} characters.");
                    
                    // Log response headers for debugging
                    Console.WriteLine($"馃攳 [DEBUG] Response Status: {response.StatusCode}");
                    Console.WriteLine($"馃攳 [DEBUG] Content-Type: {response.Content.Headers.ContentType}");
                    
                    // Save HTML for debugging purposes (optional)
                    if (html.Length > 10000) // Only save substantial content
                    {
                        await File.WriteAllTextAsync("debug_html.txt", html, System.Text.Encoding.UTF8);
                        Console.WriteLine($"馃攳 [DEBUG] HTML content saved to debug_html.txt for analysis");
                    }
                    
                    await ParseBasicHtml(html, userId);
                    return true;
                }
                else
                {
                    Console.WriteLine($"鈿狅笍 [WEAK METHOD] Failed to fetch page: {response.StatusCode}");
                    if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    {
                        Console.WriteLine("   - Status 403: Access forbidden. Cookies may be invalid or expired.");
                    }
                    else if (response.StatusCode == System.Net.HttpStatusCode.Redirect)
                    {
                        Console.WriteLine("   - Redirect detected: Likely redirecting to login page.");
                    }
                    return false;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"鉂� [WEAK METHOD] Error: {ex.Message}");
            return false;
        }
    }

    // Parse the basic HTML data and extract profile information
    static async Task ParseBasicHtml(string html, string userId)
    {
        try
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            
            // Validate authentication and privacy status
            ValidateAuthenticationStatus(html, doc);
            
            // Early return if login page or blocked
            if (IsLoginPage(html, doc))
            {
                profileData.IsPrivate = true;
                profileData.Name = "AUTHENTICATION_REQUIRED";
                return;
            }
            
            if (IsProfilePrivateOrBlocked(html, doc))
            {
                profileData.IsPrivate = true;
                profileData.Name = "PROFILE_PRIVATE_OR_BLOCKED";
                return;
            }
            
            // Extract name from title with multiple selectors
            ExtractName(doc);
            
            // Extract location with multiple selectors
            ExtractLocation(doc);
            
            // Extract about info with multiple selectors
            await ExtractAboutInfo(doc, userId);
            
            // Extract other profile information
            ExtractAdditionalInfo(doc);
            
            // Check if profile appears to be private or incomplete
            // Don't mark as private just because name wasn't found on main page
            // We'll check this again after contact info extraction
            if (string.IsNullOrEmpty(profileData.Name) || profileData.Name == "Facebook")
            {
                Console.WriteLine("鈿狅笍 Name not found on main page, will try contact pages...");
            }
            else
            {
                Console.WriteLine("鉁� Profile name extracted successfully!");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"鉂� Error parsing HTML: {ex.Message}");
        }
    }
    
    // Check if we're getting a login page instead of profile page
    static bool IsLoginPage(string html, HtmlDocument doc)
    {
        // Multiple indicators that we're on a login page
        var indicators = new[]
        {
            html.Contains("Log into Facebook"),
            html.Contains("Log In to Facebook"), 
            html.Contains("login_form"),
            html.Contains("loginbutton"),
            html.Contains("Sign up for Facebook"),
            doc.DocumentNode.SelectSingleNode("//input[@name='email']") != null,
            doc.DocumentNode.SelectSingleNode("//input[@name='pass']") != null,
            doc.GetElementbyId("login_form") != null
        };
        
        return indicators.Count(x => x) >= 2; // At least 2 indicators
    }
    
    // Enhanced privacy and visibility detection
    static bool IsProfilePrivateOrBlocked(string html, HtmlDocument doc)
    {
        var privacyIndicators = new[]
        {
            "This content isn't available",
            "Add Friend to see",
            "No info to show", 
            "Information is not available",
            "This person's information is not available",
            "This profile is private",
            "User not found",
            "Content not available",
            "This user hasn't shared any information",
            "No public information available",
            "Account suspended",
            "Profile unavailable"
        };
        
        foreach (var indicator in privacyIndicators)
        {
            if (html.Contains(indicator))
            {
                Console.WriteLine($"馃敀 Privacy/blocking detected: {indicator}");
                return true;
            }
        }
        
        // Check for empty profile indicators
        var emptyProfileSelectors = new[]
        {
            "//div[contains(@class, 'empty')]",
            "//div[contains(text(), 'Nothing to show')]",
            "//*[contains(text(), \"hasn't shared anything\")]"
        };
        
        foreach (var selector in emptyProfileSelectors)
        {
            var node = doc.DocumentNode.SelectSingleNode(selector);
            if (node != null)
            {
                Console.WriteLine($"馃敀 Empty profile detected: {node.InnerText.Trim()}");
                return true;
            }
        }
        
        return false;
    }
    
    // Validate authentication status
    static void ValidateAuthenticationStatus(string html, HtmlDocument doc)
    {
        if (IsLoginPage(html, doc))
        {
            Console.WriteLine("鉂� [AUTH CHECK] Facebook served login page - Authentication required");
            Console.WriteLine("馃敡 [SOLUTION] Your Facebook cookies have expired. To fix this:");
            Console.WriteLine("   1. Open Facebook in your browser and log in");
            Console.WriteLine("   2. Press F12 -> Network tab -> Refresh page");
            Console.WriteLine("   3. Find any Facebook request -> Copy 'Cookie' header value");
            Console.WriteLine("   4. Update the cookieString variable in Program.cs");
            Console.WriteLine("   5. Re-run the extractor");
            Console.WriteLine("   鈿狅笍 Note: The extractor code is working perfectly - only auth needed!");
        }
        else if (IsProfilePrivateOrBlocked(html, doc))
        {
            Console.WriteLine("馃敀 [PRIVACY CHECK] Profile is private or blocked");
            profileData.IsPrivate = true;
        }
        else
        {
            Console.WriteLine("鉁� [AUTH CHECK] Successfully accessing profile page");
        }
    }
    
    // Extract name using multiple selectors with mbasic-specific ones
    static void ExtractName(HtmlDocument doc)
    {
        var nameSelectors = new[]
        {
            // Priority mbasic-specific selectors
            "//div[@id='m-timeline-cover-section']//strong",
            "//div[@id='m-timeline-cover-section']//h1",
            "//title",
            
            // Additional mbasic selectors
            "//div[@id='cover-name-root']//strong",
            "//div[@id='cover-name-root']//h1",
            "//h1",
            "//strong[contains(@class, 'profileName')]",
            
            // Fallback selectors for other versions
            "//div[contains(@class, 'profileName')]",
            "//*[@id='fb-timeline-cover-name']",
            "//span[contains(@class, 'profileName')]"
        };
        
        foreach (var selector in nameSelectors)
        {
            var nameNode = doc.DocumentNode.SelectSingleNode(selector);
            if (nameNode != null && !string.IsNullOrWhiteSpace(nameNode.InnerText))
            {
                string name = nameNode.InnerText.Replace(" | Facebook", "")
                                               .Replace("| Facebook", "")
                                               .Replace("Facebook", "")
                                               .Trim();
                                               
                // Filter out login/signup text and ensure substantial content
                if (!name.Contains("Log in") && 
                    !name.Contains("Sign up") && 
                    !name.Contains("Create") &&
                    !name.Contains("Welcome") &&
                    name.Length > 1 && 
                    name.Length < 100) // Reasonable name length
                {
                    profileData.Name = name;
                    Console.WriteLine($"馃摑 Name extracted using selector '{selector}': {profileData.Name}");
                    return;
                }
            }
        }
        Console.WriteLine("鈿狅笍 No valid name found with any selector");
    }
    
    // Extract location using multiple selectors
    static void ExtractLocation(HtmlDocument doc)
    {
        var locationSelectors = new[]
        {
            "//div[contains(text(),'Lives in')]",
            "//div[contains(text(),'From')]",
            "//div[contains(@class, 'location')]",
            "//*[contains(text(),'Current City')]//following-sibling::*",
            "//*[contains(text(),'Hometown')]//following-sibling::*"
        };
        
        foreach (var selector in locationSelectors)
        {
            var locationNode = doc.DocumentNode.SelectSingleNode(selector);
            if (locationNode != null && !string.IsNullOrWhiteSpace(locationNode.InnerText))
            {
                profileData.Location = locationNode.InnerText.Trim();
                Console.WriteLine($"馃搷 Location: {profileData.Location}");
                return;
            }
        }
    }
    
    // Extract about info using multiple selectors and info page navigation
    static async Task ExtractAboutInfo(HtmlDocument doc, string userId)
    {
        // First try to extract from current page using improved selectors
        var aboutSelectors = new[]
        {
            "//div[@id='bio']", // mbasic bio section
            "//*[@id='bio']",
            "//div[contains(., 'Intro')]/following::*[1]", // mbasic intro section
            "//h3[contains(., 'Bio')]/following-sibling::div",
            "//h3[contains(., 'About')]/following-sibling::div",
            "//div[@id='about']",
            "//div[contains(@class, 'about')]",
            "//*[contains(text(), 'About')]//following-sibling::div",
            "//div[contains(@class, 'bio')]",
            "//div[contains(@class, 'intro')]",
            "//div[contains(@class, 'profileIntro')]",
            "//*[@data-overviewsection='about']",
            "//div[contains(@class, 'userContent')]"
        };
        
        foreach (var selector in aboutSelectors)
        {
            var aboutNode = doc.DocumentNode.SelectSingleNode(selector);
            if (aboutNode != null && !string.IsNullOrWhiteSpace(aboutNode.InnerText))
            {
                string aboutText = aboutNode.InnerText.Trim();
                if (aboutText.Length > 10 && !aboutText.Contains("Add Friend") && !aboutText.Contains("Message")) // Only use substantial, non-button content
                {
                    profileData.AboutInfo = aboutText;
                    Console.WriteLine($"馃摑 About Info: {profileData.AboutInfo}");
                    return;
                }
            }
        }
        
        // If no about info found on main page, try navigating to info pages
        Console.WriteLine("鈿狅笍 No About Info found on main page, trying info pages...");
        await TryInfoPageExtraction(userId);
    }
    
    // Navigate to Facebook contact and basic info pages for About content
    static async Task TryInfoPageExtraction(string userId)
    {
        // Use comprehensive URL patterns including all about sections for maximum data extraction
        var infoUrls = new[]
        {
            // Primary contact info URLs (highest priority)
            $"https://www.facebook.com/profile.php?id={userId}&sk=about_contact_and_basic_info",
            $"https://mbasic.facebook.com/profile.php?id={userId}&sk=about_contact_and_basic_info",
            
            // User-requested additional about sections
            $"https://www.facebook.com/profile.php?id={userId}&sk=about_overview",
            $"https://www.facebook.com/profile.php?id={userId}&sk=about_work_and_education", 
            $"https://www.facebook.com/profile.php?id={userId}&sk=about_places",
            $"https://www.facebook.com/profile.php?id={userId}&sk=about_details",
            $"https://www.facebook.com/profile.php?id={userId}&sk=about_family_and_relationships",
            
            // Mobile fallbacks
            $"https://mbasic.facebook.com/profile.php?id={userId}&sk=about_overview",
            $"https://mbasic.facebook.com/profile.php?id={userId}&sk=about_work_and_education",
            $"https://mbasic.facebook.com/profile.php?id={userId}&sk=about_places",
            
            // Legacy patterns  
            $"https://www.facebook.com/profile.php?id={userId}&sk=about",
            $"https://mbasic.facebook.com/profile.php?v=info&id={userId}",
            $"https://mbasic.facebook.com/profile.php?v=info&section=contact-info&id={userId}"
        };
        
        using (HttpClient client = CreateConfiguredHttpClient())
        {
            foreach (var url in infoUrls)
            {
                try
                {
                    Console.WriteLine($"馃攳 Trying contact info page: {url}");
                    
                    // Add random delay between requests to avoid rate limiting
                    await Task.Delay(new Random().Next(1000, 3000));
                    
                    var response = await client.GetAsync(url);
                    if (response.IsSuccessStatusCode)
                    {
                        // Ensure proper decompression and encoding handling for contact pages
                        byte[] contentBytes = await response.Content.ReadAsByteArrayAsync();
                        string html;
                        
                        // Handle content encoding explicitly
                        var contentEncoding = response.Content.Headers.ContentEncoding;
                        if (contentEncoding.Contains("gzip"))
                        {
                            using (var gzipStream = new System.IO.Compression.GZipStream(new MemoryStream(contentBytes), System.IO.Compression.CompressionMode.Decompress))
                            using (var reader = new StreamReader(gzipStream, System.Text.Encoding.UTF8))
                            {
                                html = await reader.ReadToEndAsync();
                            }
                        }
                        else if (contentEncoding.Contains("deflate"))
                        {
                            using (var deflateStream = new System.IO.Compression.DeflateStream(new MemoryStream(contentBytes), System.IO.Compression.CompressionMode.Decompress))
                            using (var reader = new StreamReader(deflateStream, System.Text.Encoding.UTF8))
                            {
                                html = await reader.ReadToEndAsync();
                            }
                        }
                        else
                        {
                            // No compression, decode as UTF-8
                            html = System.Text.Encoding.UTF8.GetString(contentBytes);
                        }
                        
                        Console.WriteLine($"鉁� Successfully fetched contact page with {html.Length} characters");
                        
                        // Save HTML for debugging contact info extraction (ensure proper encoding)
                        await File.WriteAllTextAsync($"contact_debug_{userId}.html", html, System.Text.Encoding.UTF8);
                        Console.WriteLine($"馃攳 Contact page HTML saved for debugging");
                        
                        var doc = new HtmlDocument();
                        doc.LoadHtml(html);
                        
                        if (await ExtractContactInfoFromPage(doc, html, userId))
                        {
                            Console.WriteLine("鉁� Successfully extracted contact info from about page!");
                            return;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"鉂� Error accessing contact info page: {ex.Message}");
                }
            }
        }
        Console.WriteLine("鈿狅笍 No contact info found on any about pages");
    }
    
    // Extract contact info from Facebook contact and basic info pages
    static async Task<bool> ExtractContactInfoFromPage(HtmlDocument doc, string html, string userId)
    {
        try
        {
            bool foundData = false;
            
            // Check for privacy indicators first
            var privacyIndicators = new[]
            {
                "This content isn't available",
                "Add Friend to see",
                "No info to show",
                "Information is not available",
                "This person's information is not available"
            };
            
            foreach (var indicator in privacyIndicators)
            {
                if (html.Contains(indicator))
                {
                    Console.WriteLine($"馃敀 Privacy detected: {indicator}");
                    profileData.IsPrivate = true;
                    return false;
                }
            }
            
            // Extract phone numbers - Enhanced for Egyptian numbers
            Console.WriteLine("馃攳 Extracting phone numbers...");
            int phonesBefore = profileData.Phones.Count;
            await ExtractPhoneNumbersFromContactPage(html);
            if (profileData.Phones.Count > phonesBefore) 
            {
                foundData = true;
                Console.WriteLine($"馃摓 [DEBUG] Total phones now: {profileData.Phones.Count}");
            }
            
            // Extract email addresses
            Console.WriteLine("馃攳 Extracting email addresses...");
            int emailsBefore = profileData.Emails.Count;
            ExtractEmailsFromContactPage(html);
            if (profileData.Emails.Count > emailsBefore) 
            {
                foundData = true;
                Console.WriteLine($"馃摟 [DEBUG] Total emails now: {profileData.Emails.Count}");
            }
            
            // Extract websites/links
            Console.WriteLine("馃攳 Extracting websites...");
            int websitesBefore = profileData.Websites.Count;
            ExtractWebsitesFromContactPage(doc, html);
            if (profileData.Websites.Count > websitesBefore) 
            {
                foundData = true;
                Console.WriteLine($"馃寪 [DEBUG] Total websites now: {profileData.Websites.Count}");
            }
            
            // Extract location information
            Console.WriteLine("馃攳 Extracting location...");
            string locationBefore = profileData.Location;
            ExtractLocationFromContactPage(doc, html);
            if (!string.IsNullOrEmpty(profileData.Location) && profileData.Location != locationBefore) 
            {
                foundData = true;
                Console.WriteLine($"馃搷 [DEBUG] Location set to: {profileData.Location}");
            }
            
            // Extract About/Bio information
            Console.WriteLine("馃攳 Extracting About info...");
            string aboutBefore = profileData.AboutInfo;
            ExtractAboutFromContactPage(doc, html);
            if (!string.IsNullOrEmpty(profileData.AboutInfo) && profileData.AboutInfo != aboutBefore) 
            {
                foundData = true;
                Console.WriteLine($"馃摑 [DEBUG] About info set: {profileData.AboutInfo.Substring(0, Math.Min(50, profileData.AboutInfo.Length))}...");
            }
            
            // Extract work information
            Console.WriteLine("馃攳 Extracting work info...");
            string workBefore = profileData.WorkInfo;
            ExtractWorkFromContactPage(doc, html);
            if (!string.IsNullOrEmpty(profileData.WorkInfo) && profileData.WorkInfo != workBefore) 
            {
                foundData = true;
                Console.WriteLine($"馃捈 [DEBUG] Work info set to: {profileData.WorkInfo}");
            }
            
            // Extract education information
            Console.WriteLine("馃攳 Extracting education info...");
            string educationBefore = profileData.EducationInfo;
            ExtractEducationFromContactPage(doc, html);
            if (!string.IsNullOrEmpty(profileData.EducationInfo) && profileData.EducationInfo != educationBefore) 
            {
                foundData = true;
                Console.WriteLine($"馃帗 [DEBUG] Education info set to: {profileData.EducationInfo}");
            }
            
            return foundData;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"鉂� Error extracting contact info: {ex.Message}");
            return false;
        }
    }
    
    // Extract phone numbers specifically for Egyptian numbers
    static async Task ExtractPhoneNumbersFromContactPage(string html)
    {
        try
        {
            var phonePatterns = new[]
            {
                // Egyptian phone patterns
                @"\+?20\s?1[0-5]\d{8}",  // Egyptian mobile (+20 10/11/12/15 XXXXXXXX)
                @"\+?20\s?[23][0-9]\d{7}", // Egyptian landline (+20 2X/3X XXXXXXX)
                @"01[0-5]\d{8}",          // Egyptian mobile without country code
                @"[23][0-9]\d{7}",        // Egyptian landline without country code
                // General international patterns
                @"\+\d{1,3}[\s.-]?\d{1,4}[\s.-]?\d{1,4}[\s.-]?\d{1,4}[\s.-]?\d{1,4}",
                @"\(\d{1,4}\)[\s.-]?\d{1,4}[\s.-]?\d{1,4}[\s.-]?\d{1,4}"
            };
            
            var foundNumbers = new HashSet<string>();
            
            foreach (var pattern in phonePatterns)
            {
                var regex = new Regex(pattern, RegexOptions.IgnoreCase);
                var matches = regex.Matches(html);
                
                foreach (Match match in matches)
                {
                    string rawPhone = match.Value.Trim();
                    
                    // Clean the number
                    rawPhone = rawPhone.Replace(" ", "").Replace("-", "").Replace("(", "").Replace(")", "").Replace(".", "");
                    
                    if (rawPhone.Length >= 8) // Minimum reasonable phone length
                    {
                        foundNumbers.Add(rawPhone);
                    }
                }
            }
            
            // Validate and format Egyptian numbers
            foreach (string phone in foundNumbers)
            {
                try
                {
                    // Try parsing as Egyptian number first
                    var number = phoneUtil.Parse(phone, "EG");
                    
                    if (phoneUtil.IsValidNumber(number))
                    {
                        string formattedPhone = phoneUtil.Format(number, PhoneNumberFormat.INTERNATIONAL);
                        if (!profileData.Phones.Contains(formattedPhone))
                        {
                            profileData.Phones.Add(formattedPhone);
                            Console.WriteLine($"馃摓 Found phone: {formattedPhone}");
                        }
                    }
                }
                catch
                {
                    // If Egyptian parsing fails, try without country code
                    if (phone.StartsWith("01") && phone.Length == 11) // Egyptian mobile format
                    {
                        string egyptianPhone = "+20" + phone;
                        if (!profileData.Phones.Contains(egyptianPhone))
                        {
                            profileData.Phones.Add(egyptianPhone);
                            Console.WriteLine($"馃摓 Found Egyptian phone: {egyptianPhone}");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"鉂� Error extracting phones: {ex.Message}");
        }
    }
    
    // Extract email addresses from contact page
    static void ExtractEmailsFromContactPage(string html)
    {
        try
        {
            // More precise email regex to avoid false positives
            var emailRegex = new Regex(@"\b[A-Za-z0-9._%+-]{2,}@[A-Za-z0-9.-]{2,}\.[A-Za-z]{2,6}\b", RegexOptions.IgnoreCase);
            var matches = emailRegex.Matches(html);
            
            // Common valid email domains to prioritize
            var validDomains = new[] { "gmail.com", "yahoo.com", "hotmail.com", "outlook.com", "live.com", 
                                     "aol.com", "icloud.com", "protonmail.com", "yandex.com", "mail.com" };
            
            foreach (Match match in matches)
            {
                string email = match.Value.ToLower();
                
                // Skip obvious non-email patterns
                if (email.Contains("facebook.com") || email.Contains("example.com") || 
                    email.Contains("test.com") || email.Contains("noreply") ||
                    email.Length < 6 || email.StartsWith("no-") || email.Contains("@.") ||
                    profileData.Emails.Contains(email))
                    continue;
                
                // Validate email format more strictly
                if (IsValidEmailFormat(email))
                {
                    profileData.Emails.Add(email);
                    Console.WriteLine($"馃摟 Found email: {email}");
                    
                    // If it's a common domain, note it's likely legitimate
                    if (validDomains.Any(domain => email.EndsWith(domain)))
                    {
                        Console.WriteLine($"鉁� [EMAIL VERIFIED] {email} - Common domain detected");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"鉂� Error extracting emails: {ex.Message}");
        }
    }
    
    // Additional email validation to prevent false positives like iq@wc.ae
    static bool IsValidEmailFormat(string email)
    {
        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            var parts = email.Split('@');
            
            // Local part should be at least 2 characters
            if (parts[0].Length < 2) return false;
            
            // Domain should have at least one dot and valid structure
            var domain = parts[1];
            if (!domain.Contains('.') || domain.StartsWith('.') || domain.EndsWith('.')) return false;
            
            // Domain parts should be reasonable length
            var domainParts = domain.Split('.');
            if (domainParts.Any(part => part.Length < 2)) return false;
            
            return addr.Address == email;
        }
        catch
        {
            return false;
        }
    }
    
    // Extract websites from contact page
    static void ExtractWebsitesFromContactPage(HtmlDocument doc, string html)
    {
        try
        {
            // Look for website/URL patterns
            var urlRegex = new Regex(@"https?://[^\s<>""']+", RegexOptions.IgnoreCase);
            var matches = urlRegex.Matches(html);
            
            foreach (Match match in matches)
            {
                string url = match.Value;
                if (!url.Contains("facebook.com") && !url.Contains("fb.com") && !profileData.Websites.Contains(url))
                {
                    profileData.Websites.Add(url);
                    Console.WriteLine($"馃寪 Found website: {url}");
                }
            }
            
            // Also look for link elements
            var linkNodes = doc.DocumentNode.SelectNodes("//a[@href]");
            if (linkNodes != null)
            {
                foreach (var linkNode in linkNodes)
                {
                    string href = linkNode.GetAttributeValue("href", "");
                    if (href.StartsWith("http") && !href.Contains("facebook.com") && !href.Contains("fb.com") && !profileData.Websites.Contains(href))
                    {
                        profileData.Websites.Add(href);
                        Console.WriteLine($"馃寪 Found website: {href}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"鉂� Error extracting websites: {ex.Message}");
        }
    }
    
    // Extract location from contact page
    static void ExtractLocationFromContactPage(HtmlDocument doc, string html)
    {
        try
        {
            var locationSelectors = new[]
            {
                "//*[contains(text(),'Lives in')]/following-sibling::*",
                "//*[contains(text(),'From')]/following-sibling::*",
                "//*[contains(text(),'Current City')]/following-sibling::*",
                "//*[contains(text(),'Hometown')]/following-sibling::*",
                "//div[contains(@data-overviewsection,'places')]",
                "//*[contains(text(),'Location')]/following-sibling::*"
            };
            
            foreach (var selector in locationSelectors)
            {
                var locationNode = doc.DocumentNode.SelectSingleNode(selector);
                if (locationNode != null && !string.IsNullOrWhiteSpace(locationNode.InnerText))
                {
                    string location = locationNode.InnerText.Trim();
                    if (location.Length > 2 && !location.Contains("Add") && !location.Contains("Edit"))
                    {
                        profileData.Location = location;
                        Console.WriteLine($"馃搷 Found location: {profileData.Location}");
                        return;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"鉂� Error extracting location: {ex.Message}");
        }
    }
    
    // Extract About/Bio from contact page
    static void ExtractAboutFromContactPage(HtmlDocument doc, string html)
    {
        try
        {
            // Enhanced Bio/About selectors for comprehensive extraction
            var aboutSelectors = new[]
            {
                // Direct Bio sections
                "//h3[contains(., 'Bio')]/following-sibling::div",
                "//h3[contains(., 'About')]/following-sibling::div", 
                "//h4[contains(., 'Bio')]/following-sibling::div",
                "//h4[contains(., 'About')]/following-sibling::div",
                "//*[contains(text(),'Bio:')]/following-sibling::*",
                "//*[contains(text(),'About:')]/following-sibling::*",
                
                // Overview and detail sections
                "//div[contains(@data-overviewsection,'about')]",
                "//div[contains(@data-overviewsection,'overview')]",
                "//*[@data-section='about']",
                "//*[@data-section='overview']",
                
                // Profile intro/description areas
                "//div[contains(@class,'profileIntro')]//div[contains(@class,'text')]",
                "//div[contains(@class,'intro')]//div[not(contains(@class,'button'))]",
                "//div[contains(@class,'description')]",
                "//div[contains(@class,'bio')]",
                "//div[contains(@class,'about')]//p",
                
                // Mobile-specific patterns  
                "//div[@id='bio']//div[not(contains(@class,'button'))]",
                "//div[@id='about']//div[not(contains(@class,'button'))]",
                "//*[@id='intro']//div[text()]",
                
                // Text content near Bio/About keywords
                "//*[contains(text(),'Biography')]/following-sibling::div",
                "//*[contains(text(),'Description')]/following-sibling::div",
                "//span[contains(text(),'Bio')]/parent::*/following-sibling::div",
                
                // Generic content areas that might contain Bio
                "//div[contains(@class,'userContent')]//p[string-length(text()) > 20]",
                "//div[contains(@class,'content')]//p[string-length(text()) > 20]"
            };
            
            foreach (var selector in aboutSelectors)
            {
                var aboutNode = doc.DocumentNode.SelectSingleNode(selector);
                if (aboutNode != null && !string.IsNullOrWhiteSpace(aboutNode.InnerText))
                {
                    string about = CleanAboutText(aboutNode.InnerText.Trim());
                    if (IsValidAboutContent(about))
                    {
                        profileData.AboutInfo = about;
                        Console.WriteLine($"馃摑 [BIO FOUND] {profileData.AboutInfo.Substring(0, Math.Min(100, profileData.AboutInfo.Length))}...");
                        return;
                    }
                }
            }
            
            // Also try searching in raw HTML for Bio patterns
            ExtractBioFromRawHtml(html);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"鉂� Error extracting about: {ex.Message}");
        }
    }
    
    // Clean and validate Bio/About content  
    static string CleanAboutText(string text)
    {
        // Remove common UI elements and clean up text
        text = Regex.Replace(text, @"(Add Friend|Message|Follow|Like|Share|Edit|Delete|Report)", "", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\s+", " "); // Normalize whitespace
        return text.Trim();
    }
    
    static bool IsValidAboutContent(string about)
    {
        return about.Length > 15 && 
               !about.ToLower().Contains("add friend") && 
               !about.ToLower().Contains("message") &&
               !about.ToLower().Contains("edit profile") &&
               !about.All(char.IsDigit) && // Not just numbers
               about.Split(' ').Length > 3; // More than 3 words
    }
    
    // Extract Bio from raw HTML when structured selectors fail
    static void ExtractBioFromRawHtml(string html)
    {
        try
        {
            // Look for Bio patterns in raw HTML
            var bioPatterns = new[]
            {
                @"(?i)bio['""\s]*:[\s]*['""]([^'""]{15,200})['""]",
                @"(?i)about['""\s]*:[\s]*['""]([^'""]{15,200})['""]", 
                @"(?i)description['""\s]*:[\s]*['""]([^'""]{15,200})['""]",
                @"(?i)intro['""\s]*:[\s]*['""]([^'""]{15,200})['""]"
            };
            
            foreach (var pattern in bioPatterns)
            {
                var match = Regex.Match(html, pattern);
                if (match.Success)
                {
                    string bio = CleanAboutText(match.Groups[1].Value);
                    if (IsValidAboutContent(bio))
                    {
                        profileData.AboutInfo = bio;
                        Console.WriteLine($"馃摑 [BIO FROM RAW HTML] {bio.Substring(0, Math.Min(100, bio.Length))}...");
                        return;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"鉂� Error extracting bio from raw HTML: {ex.Message}");
        }
    }
    
    // Extract work info from contact page
    static void ExtractWorkFromContactPage(HtmlDocument doc, string html)
    {
        try
        {
            var workSelectors = new[]
            {
                "//h3[contains(., 'Work')]/following-sibling::div",
                "//*[contains(text(),'Works at')]/following-sibling::*",
                "//*[contains(text(),'Job:')]/following-sibling::*",
                "//div[contains(@data-overviewsection,'work')]"
            };
            
            foreach (var selector in workSelectors)
            {
                var workNode = doc.DocumentNode.SelectSingleNode(selector);
                if (workNode != null && !string.IsNullOrWhiteSpace(workNode.InnerText))
                {
                    string work = workNode.InnerText.Trim();
                    if (work.Length > 5 && !work.Contains("Add") && !work.Contains("Edit"))
                    {
                        profileData.WorkInfo = work;
                        Console.WriteLine($"馃捈 Found work: {profileData.WorkInfo}");
                        return;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"鉂� Error extracting work: {ex.Message}");
        }
    }
    
    // Extract education from contact page
    static void ExtractEducationFromContactPage(HtmlDocument doc, string html)
    {
        try
        {
            var educationSelectors = new[]
            {
                "//h3[contains(., 'Education')]/following-sibling::div",
                "//*[contains(text(),'Studied at')]/following-sibling::*",
                "//*[contains(text(),'School:')]/following-sibling::*",
                "//div[contains(@data-overviewsection,'education')]"
            };
            
            foreach (var selector in educationSelectors)
            {
                var eduNode = doc.DocumentNode.SelectSingleNode(selector);
                if (eduNode != null && !string.IsNullOrWhiteSpace(eduNode.InnerText))
                {
                    string education = eduNode.InnerText.Trim();
                    if (education.Length > 5 && !education.Contains("Add") && !education.Contains("Edit"))
                    {
                        profileData.EducationInfo = education;
                        Console.WriteLine($"馃帗 Found education: {profileData.EducationInfo}");
                        return;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"鉂� Error extracting education: {ex.Message}");
        }
    }
    
    // Extract additional profile information
    static void ExtractAdditionalInfo(HtmlDocument doc)
    {
        // Try to extract work info
        var workSelectors = new[]
        {
            "//*[contains(text(),'Works at')]",
            "//*[contains(text(),'Work')]//following-sibling::*",
            "//div[contains(@class, 'work')]"
        };
        
        foreach (var selector in workSelectors)
        {
            var workNode = doc.DocumentNode.SelectSingleNode(selector);
            if (workNode != null && !string.IsNullOrWhiteSpace(workNode.InnerText))
            {
                profileData.WorkInfo = workNode.InnerText.Trim();
                Console.WriteLine($"馃捈 Work Info: {profileData.WorkInfo}");
                break;
            }
        }
        
        // Try to extract education info
        var educationSelectors = new[]
        {
            "//*[contains(text(),'Studied at')]",
            "//*[contains(text(),'Education')]//following-sibling::*",
            "//div[contains(@class, 'education')]"
        };
        
        foreach (var selector in educationSelectors)
        {
            var eduNode = doc.DocumentNode.SelectSingleNode(selector);
            if (eduNode != null && !string.IsNullOrWhiteSpace(eduNode.InnerText))
            {
                profileData.EducationInfo = eduNode.InnerText.Trim();
                Console.WriteLine($"馃帗 Education Info: {profileData.EducationInfo}");
                break;
            }
        }
    }

    // Strong Method: Advanced scraping with validation
    static async Task StrongMethod(string userId)
    {
        Console.WriteLine("馃攳 [STRONG METHOD] Attempting advanced scraping...");
        if (!string.IsNullOrEmpty(accessToken))
        {
            await TryGraphAPI(userId);
        }
        await StealthyMobileScraping(userId);
        
        // Always try comprehensive contact info extraction from dedicated contact pages
        Console.WriteLine("馃攳 [STRONG METHOD] Attempting comprehensive contact info extraction...");
        await TryInfoPageExtraction(userId);
    }

    // Try Graph API for basic information (Name, Email, Website)
    static async Task TryGraphAPI(string userId)
    {
        try
        {
            Console.WriteLine("馃攳 [GRAPH API] Attempting Graph API extraction...");
            using (HttpClient client = new HttpClient())
            {
                string graphUrl = $"https://graph.facebook.com/{userId}?fields=name,about,email,website,birthday,location&access_token={accessToken}";
                var response = await client.GetAsync(graphUrl);
                if (response.IsSuccessStatusCode)
                {
                    string jsonData = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"鉁� [GRAPH API] Success: {jsonData.Length} characters");
                    using (JsonDocument doc = JsonDocument.Parse(jsonData))
                    {
                        var root = doc.RootElement;
                        if (root.TryGetProperty("name", out var nameElement))
                        {
                            profileData.Name = nameElement.GetString() ?? "";
                            Console.WriteLine($"馃摑 [NAME] {profileData.Name}");
                        }
                        if (root.TryGetProperty("email", out var emailElement))
                        {
                            string email = emailElement.GetString() ?? "";
                            if (!string.IsNullOrEmpty(email))
                            {
                                profileData.Emails.Add(email);
                                Console.WriteLine($"馃摟 [EMAIL] {email}");
                            }
                        }
                        if (root.TryGetProperty("website", out var websiteElement))
                        {
                            string website = websiteElement.GetString() ?? "";
                            if (!string.IsNullOrEmpty(website))
                            {
                                profileData.Websites.Add(website);
                                Console.WriteLine($"馃寪 [WEBSITE] {website}");
                            }
                        }
                        if (root.TryGetProperty("birthday", out var birthdayElement))
                        {
                            profileData.Birthday = birthdayElement.GetString() ?? "";
                            Console.WriteLine($"馃巶 [BIRTHDAY] {profileData.Birthday}");
                        }
                        if (root.TryGetProperty("location", out var locationElement))
                        {
                            profileData.Location = locationElement.GetString() ?? "";
                            Console.WriteLine($"馃搷 [LOCATION] {profileData.Location}");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"鉂� [GRAPH API] Error: {ex.Message}");
        }
    }

    // Create a properly configured HTTP client with all necessary headers and decompression
    static HttpClient CreateConfiguredHttpClient()
    {
        // Create handler with comprehensive automatic decompression for all content types
        var handler = new HttpClientHandler()
        {
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate | System.Net.DecompressionMethods.Brotli
        };
        
        var client = new HttpClient(handler);
        
        // Enhanced headers to mimic real browser requests more effectively
        client.DefaultRequestHeaders.Add("User-Agent", GetRandomMobileUserAgent());
        client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8");
        client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9,ar;q=0.8");
        client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br");
        client.DefaultRequestHeaders.Add("DNT", "1"); // Do Not Track
        client.DefaultRequestHeaders.Add("Connection", "keep-alive");
        client.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");
        client.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "document");
        client.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "navigate");
        client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "same-origin");
        client.DefaultRequestHeaders.Add("Sec-Fetch-User", "?1");
        client.DefaultRequestHeaders.Add("Cache-Control", "max-age=0");
        client.DefaultRequestHeaders.Add("Sec-Ch-Ua", "\"Not A(Brand\";v=\"99\", \"Google Chrome\";v=\"121\", \"Chromium\";v=\"121\"");
        client.DefaultRequestHeaders.Add("Sec-Ch-Ua-Mobile", "?1");
        client.DefaultRequestHeaders.Add("Sec-Ch-Ua-Platform", "\"Android\"");
        
        // Add cookies if available
        if (!string.IsNullOrEmpty(cookieString))
        {
            client.DefaultRequestHeaders.Add("Cookie", cookieString);
            Console.WriteLine("馃崻 [COOKIES] Using provided cookie string for authentication");
        }
        else
        {
            Console.WriteLine("鈿狅笍 [COOKIES] No cookies provided - may get login page");
        }
        
        // Set timeout
        client.Timeout = TimeSpan.FromSeconds(30);
        
        return client;
    }

    // Perform mobile scraping with stealthier techniques
    static async Task StealthyMobileScraping(string userId)
    {
        string url = $"https://mbasic.facebook.com/{userId}";
        using (HttpClient client = CreateConfiguredHttpClient())
        {
            var response = await client.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                string html = await response.Content.ReadAsStringAsync();
                await ExtractAllPhones(new HtmlDocument(), html);
            }
            else
            {
                Console.WriteLine($"鈿狅笍 [STEALTHY] Failed to fetch page: {response.StatusCode}");
            }
        }
    }

    // Extract all numbers and then apply the filter at the end
    static async Task ExtractAllPhones(HtmlDocument doc, string html)
    {
        try
        {
            Console.WriteLine("馃攷 Starting broad phone number extraction...");
            var rawNumbers = new HashSet<string>();
            var validatedPhones = new HashSet<string>();

            // This is the broad regex from your original script that found more numbers
            var phoneRegex = new Regex(@"\+?(\(?\d{1,3}\)?[\s.-]?)?\(?(\d{1,4})\)?[\s.-]?\d{1,4}[\s.-]?\d{1,4}", RegexOptions.Compiled);
            var matches = phoneRegex.Matches(html);

            Console.WriteLine($"Found {matches.Count} potential raw number strings.");

            // First pass: extract all raw numbers from the HTML
            foreach (Match match in matches)
            {
                rawNumbers.Add(match.Value);
            }

            Console.WriteLine($"Filtering {rawNumbers.Count} unique raw numbers...");

            // Second pass: filter and validate each number individually
            foreach (string rawPhone in rawNumbers)
            {
                Console.WriteLine($"\n  - Processing raw string: '{rawPhone}'...");
                try
                {
                    // Attempt to parse the number, using "EG" as the default region
                    var number = phoneUtil.Parse(rawPhone, "EG");

                    if (phoneUtil.IsValidNumber(number))
                    {
                        Console.WriteLine("    -> Is a valid number. 鉁�");

                        // Check if the number is of type MOBILE
                        if (phoneUtil.GetNumberType(number) == PhoneNumberType.MOBILE)
                        {
                            Console.WriteLine("    -> Is a mobile number. 鉁�");
                            string formattedPhone = phoneUtil.Format(number, PhoneNumberFormat.INTERNATIONAL);

                            // Only add if it's a unique, valid mobile number
                            if (validatedPhones.Add(formattedPhone))
                            {
                                profileData.Phones.Add(formattedPhone);
                                Console.WriteLine($"    -> ADDED: {formattedPhone}");
                            }
                            else
                            {
                                Console.WriteLine("    -> Already added, skipping.");
                            }
                        }
                        else
                        {
                            Console.WriteLine($"    -> Is not a mobile number (Type: {phoneUtil.GetNumberType(number)}). 鉂�");
                        }
                    }
                    else
                    {
                        Console.WriteLine("    -> Is not a valid number. 鉂�");
                    }
                }
                catch (NumberParseException ex)
                {
                    Console.WriteLine($"    -> Failed to parse. Error: {ex.ErrorType}. 鉂�");
                }
            }
            Console.WriteLine($"\n馃摓 Finished. Total unique valid mobile numbers found: {profileData.Phones.Count}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"鉂� Error extracting phone numbers: {ex.Message}");
        }
    }

    // Check final privacy status based on all extracted data
    static void CheckFinalPrivacyStatus()
    {
        bool hasData = !string.IsNullOrEmpty(profileData.Name) ||
                       profileData.Phones.Count > 0 ||
                       profileData.Emails.Count > 0 ||
                       profileData.Websites.Count > 0 ||
                       !string.IsNullOrEmpty(profileData.WorkInfo) ||
                       !string.IsNullOrEmpty(profileData.EducationInfo) ||
                       !string.IsNullOrEmpty(profileData.Location) ||
                       !string.IsNullOrEmpty(profileData.Birthday) ||
                       !string.IsNullOrEmpty(profileData.AboutInfo) ||
                       !string.IsNullOrEmpty(profileData.Relationship);
        
        profileData.IsPrivate = !hasData;
        
        Console.WriteLine($"\n馃攳 [FINAL STATUS DEBUG]");
        Console.WriteLine($"   Name: '{profileData.Name}' (Empty: {string.IsNullOrEmpty(profileData.Name)})");
        Console.WriteLine($"   Phones: {profileData.Phones.Count} found - {string.Join(", ", profileData.Phones)}");
        Console.WriteLine($"   Emails: {profileData.Emails.Count} found - {string.Join(", ", profileData.Emails)}");
        Console.WriteLine($"   Websites: {profileData.Websites.Count} found - {string.Join(", ", profileData.Websites)}");
        Console.WriteLine($"   Location: '{profileData.Location}' (Empty: {string.IsNullOrEmpty(profileData.Location)})");
        Console.WriteLine($"   Birthday: '{profileData.Birthday}' (Empty: {string.IsNullOrEmpty(profileData.Birthday)})");
        Console.WriteLine($"   About Info: '{profileData.AboutInfo}' (Empty: {string.IsNullOrEmpty(profileData.AboutInfo)})");
        Console.WriteLine($"   Work Info: '{profileData.WorkInfo}' (Empty: {string.IsNullOrEmpty(profileData.WorkInfo)})");
        Console.WriteLine($"   Education Info: '{profileData.EducationInfo}' (Empty: {string.IsNullOrEmpty(profileData.EducationInfo)})");
        Console.WriteLine($"   Relationship: '{profileData.Relationship}' (Empty: {string.IsNullOrEmpty(profileData.Relationship)})");
        Console.WriteLine($"   Has Any Data: {hasData}");
        Console.WriteLine($"   Profile is Private: {profileData.IsPrivate}");
        
        if (hasData)
        {
            Console.WriteLine("鉁� Profile has extractable data - NOT marked as private");
        }
        else
        {
            Console.WriteLine("鈿狅笍 No data extracted - profile appears to be private or inaccessible");
        }
    }

    // Save profile data to text file
    static async Task GenerateDynamicOutput()
    {
        try
        {
            string outputFilePath = "data.txt";
            string profileInfo = $"Name: {profileData.Name}\n" +
                                 $"Phones: {string.Join(", ", profileData.Phones)}\n" +
                                 $"Emails: {string.Join(", ", profileData.Emails)}\n" +
                                 $"Websites: {string.Join(", ", profileData.Websites)}\n" +
                                 $"Work Info: {profileData.WorkInfo}\n" +
                                 $"Education Info: {profileData.EducationInfo}\n" +
                                 $"Location: {profileData.Location}\n" +
                                 $"Birthday: {profileData.Birthday}\n" +
                                 $"Relationship: {profileData.Relationship}\n" +
                                 $"About Info: {profileData.AboutInfo}\n" +
                                 $"Profile is Private: {profileData.IsPrivate}\n";
            await File.WriteAllTextAsync(outputFilePath, profileInfo);
            Console.WriteLine($"馃搫 [SAVE DEBUG] Writing to {outputFilePath}:");
            Console.WriteLine($"馃搫 Content length: {profileInfo.Length} characters");
            Console.WriteLine($"馃搫 Content preview: {profileInfo.Substring(0, Math.Min(200, profileInfo.Length))}...");
            Console.WriteLine($"鉁� Profile saved to {outputFilePath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"鉂� Error generating output: {ex.Message}");
        }
    }

    // Get random user agent for stealth scraping
    static string GetRandomMobileUserAgent()
    {
        string[] userAgents = new[]
        {
            // Recent Chrome mobile versions
            "Mozilla/5.0 (Linux; Android 13; SM-S908B) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/112.0.0.0 Mobile Safari/537.36",
            "Mozilla/5.0 (Linux; Android 12; SM-G991B) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/111.0.0.0 Mobile Safari/537.36",
            "Mozilla/5.0 (Linux; Android 11; SM-G975F) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/110.0.0.0 Mobile Safari/537.36",
            
            // Recent iPhone Safari versions
            "Mozilla/5.0 (iPhone; CPU iPhone OS 16_3 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/16.3 Mobile/15E148 Safari/604.1",
            "Mozilla/5.0 (iPhone; CPU iPhone OS 15_6 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/15.6 Mobile/15E148 Safari/604.1",
            
            // Recent Firefox mobile
            "Mozilla/5.0 (Mobile; rv:109.0) Gecko/109.0 Firefox/115.0",
            
            // Samsung Internet
            "Mozilla/5.0 (Linux; Android 13; SAMSUNG SM-S918B) AppleWebKit/537.36 (KHTML, like Gecko) SamsungBrowser/20.0 Chrome/106.0.5249.126 Mobile Safari/537.36",
            
            // Facebook's own mobile browser (fixed format)
            "Mozilla/5.0 (Linux; Android 12; SM-G991B) AppleWebKit/537.36 (KHTML, like Gecko) Version/4.0 Chrome/111.0.5563.116 Mobile Safari/537.36 FBAN/EMA FBLC/en_US FBAV/339.0.0.11.116"
        };
        var random = new Random();
        return userAgents[random.Next(userAgents.Length)];
    }
}








Publicity marketing (Trend)



For Memes check: Market Memes

1-Sharing a meme about your free coupons
2-Sharing a meme about your clothes (for example people were making fun of those who wear calvin klein are meant for fancy places while cottonelle meant for low budget places, while those who wear nothing are meant to go home.. Try to add your Suncore product in this meme)
















Emotional Branding Marketing
“Used by: Coca-Cola, Apple Focuses on creating an emotional connection between the brand and the consumer (e.g., inspiration, belonging, happiness).”
























Lifestyle Marketing
“Used by: Red Bull, Starbucks and GoPro Sell the lifestyle or experience surrounding the product — not just the product itself.”
























Influencer & Celebrity Marketing
“Used by: Adidas, Pepsi, Calvin Klein Leveraging trusted figures with large followings to promote products.”
























Content Marketing
“Used by: HubSpot, Apple, LEGO Providing valuable or entertaining content to build trust and keep customers engaged such as Lego YT Movie Channel videos.”
























Product Placement
“Used by: Coca-Cola, BMW, Ray-Ban Integrating products into movies, shows, music videos, or games.”












































































 Market Analysis

“First we need to decide which layer we are targeting and will reach them out by Social Media, TikTok, Facebook and Famous people”

 









1-	We are targeting these people: 
1- Work Haters (Coupons role)
Unemployed	Those who are looking for money
Gamblers 	Those who are looking to get rich fast
Drug consumers	Those are also looking for easy-money to buy drugs
Urgent debts	Those who have debts and wants quick money
Single parents	Those who can’t work a lot but still struggling 
Teens	Those doing survey-for-cash apps or game-to-earn platforms

2- Work Lovers (Arm Sleeve UV Protection)
Technicians boys	Teens who work %90 under sun for assisting technicians
Talabat workers ⭐	This layer will buy a lot of our product
Taxis	Their hand gets burned by the sun while driving
Constructions	Those who build houses


3- Others (Arm Sleeve UV Protection)
Collage students	Helwan Students uses sun cream


















2-	Marketing Content based on Layer Needs
1- Coupons

	

2- Arm Sleeve UV Protection

Talabat	Sun-stroke, skin burn, cools, no sweating, good body health, good mood
Tech Boys	Sun-stroke, skin burn, cools, no sweating, good body health, good mood
Taxis	Skin burn, cools, no sweating, good body health, good mood, AC savings
Constructions	Sun-stroke, skin burn, cools, no sweating, good body health, good mood
Collage students	Sun-stroke, skin burn, cools, no sweating, good body health, good mood



Content: 
Sun-Stroke	1.	Fainting/Loss of Consciousness
2.	Organ Damage (e.g., brain, heart, kidneys, muscles)
3.	Seizures
4.	Confusion/Delirium
5.	Death
6.	Rapid heartbeat and breathing
7.	Skin hot and dry to the touch (even without sweating)
8.	Nausea or vomiting
Skin  Burn	Pain & Redness
Blistering
Increased sensitivity to touch or temperature
Scarring & Discoloration
Loss of Skin Elasticity
Nerve Damage (in severe cases)
Potential nerve damage with deeper burns

Cools	Moisture-Wicking Fabric: Draws sweat away from your skin.
Evaporative Cooling: Sweat evaporates from the fabric, creating a chilling effect.
Breathable Weave: Allows air circulation, preventing heat buildup.
UV Barrier: Blocks direct sun, reducing heat absorption by the skin.
Cooling technology activates with sweat
No Sweating	
Good body health	
Good mood	
AC savings	
	

 
Description about the company:

FAQ
•	What is Suncore ?
	Suncore is a company was designed exclusively for "UV impact on skin" & providing protection against sunstroke and heat-induced fainting.

•	What in it for me in your products?
	Suncore isn't just about protection — it prioritizes pleasure, ease of use and comfort. Designed as a stylish medical product, Suncore blends functionality with elegance, offering a unique balance of care and sophistication.

•	How does it work?
	Suncore UV-protective arm sleeve shields your skin from harmful UV rays while transforming heat into a refreshing cooling sensation. Designed to minimize sweating and maximize comfort, its ultra-soft fabric enhances your mood, helping you perform at your best in daily activities—whether at the gym, on construction sites, working as a technician, street vendor, or taxi driver.



Products

 

Suncore UV-protective arm sleeve 
•	Shields your skin from harmful UV rays.
•	Transforming heat into a refreshing cooling sensation. 
•	Designed to minimize sweating and maximize comfort
•	Ultra-soft fabric enhances your mood, helping you perform at your best in daily activities, whether at Gym, Construction sites, Working as a technician, Street vendor & Taxi driver.






Market Memes

“Market memes is how we will use Publicity / Trend Strategy which is by creating memes on social media”

1-Suncore cream is meant for protecting your skin from going dark.. Not meant for niggas
2-




Market Product

“Try creating more than one product to increase the number of sales and low competition chance and being unique and specify the needs for every layer”

(Nylon + Spandex) or (Polyesteer + Spandex)

Try creating 2 types of Arm Sleeve UV protection:
1- Sun arm sleeve (turns sun heat into cool)
2-AC arm sleeve (turns air into cool)

Sun arm sleeve:
For those who faces the sun for long time

AC arm sleeve:
Those who work in office or taxi with no AC but has air.. AC arm sleeve will transform air into cooling sensation




“so i learned about publicity marketing strategy and guerilla marketing strategy.. is there any famous and known and effective strategy that big brands relied on ?”


Gemini:
You're asking about the cornerstone strategies that underpin the success of major global brands beyond the more tactical "publicity" (which often falls under PR) and "guerrilla marketing" (which is more about unconventional, low-cost tactics).
Big brands rely on sophisticated, multi-faceted strategies to build enduring dominance. Here are some of the most famous, known, and highly effective ones:
1.	Brand Marketing (or Brand Building):
a.	What it is: This is the most fundamental strategy. It's about shaping how consumers perceive your brand, what it stands for, and the emotional connection they have with it. It focuses on creating a unique brand identity, values, personality, and promise that resonates deeply with the target audience. It's not just about selling a product, but selling an idea, a feeling, or a lifestyle.
b.	Why it's effective: A strong brand commands loyalty, allows for premium pricing, fosters trust, and provides a competitive advantage that's hard to replicate.
c.	Famous Examples: 
i.	Nike: "Just Do It." They sell aspiration, athletic achievement, and personal empowerment, not just shoes.
ii.	Apple: They sell innovation, simplicity, creativity, and status, not just devices.
iii.	Coca-Cola: They sell happiness, togetherness, and refreshment, not just a soda.
2.	Content Marketing:
a.	What it is: Creating and distributing valuable, relevant, and consistent content (articles, videos, infographics, podcasts, social media posts, etc.) to attract and retain a clearly defined audience, and ultimately, to drive profitable customer action. It educates, entertains, or inspires without overt selling.
b.	Why it's effective: Builds trust and credibility, establishes the brand as an authority, improves SEO, and nurtures leads over time.
c.	Famous Examples: 
i.	Red Bull: As we discussed, they are a master of content marketing through their Red Bull Media House, producing extreme sports videos, documentaries, and events that align with their adventurous brand.
ii.	HubSpot: A software company that built its empire on extensive content marketing (blogs, guides, templates) to educate businesses about inbound marketing.
3.	Experiential Marketing (or Engagement Marketing):
a.	What it is: Creating immersive, memorable, and often interactive experiences for consumers that allow them to physically or emotionally engage with the brand in a real-world setting. It goes beyond traditional advertising to create a "wow" factor and a direct connection.
b.	Why it's effective: Generates strong emotional bonds, creates shareable moments, drives word-of-mouth, and can lead to viral exposure.
c.	Famous Examples: 
i.	Starbucks: Crafting the "third place" experience with unique store ambiance, personalized drinks, and a consistent atmosphere.
ii.	Coca-Cola: Their "Share a Coke" campaigns where bottles were personalized, or interactive vending machines that offered unique experiences.
4.	Integrated Marketing Communications (IMC):
a.	What it is: A holistic approach that ensures all communication channels (advertising, PR, social media, content, sales promotions, direct marketing, etc.) work together seamlessly to deliver a clear, consistent, and compelling message about the brand.
b.	Why it's effective: Maximizes impact and efficiency by ensuring all touchpoints reinforce the same brand message, preventing confusion and building a strong, unified brand image.
c.	Famous Examples: Almost every major global brand uses IMC. Think about how Apple's product launches, website, stores, and advertising all have a consistent minimalist aesthetic and message of innovation.
5.	Digital Marketing (Paid Search, Social Media Marketing, SEO):
a.	What it is: An umbrella term for marketing efforts that use an electronic device or the internet. This includes: 
i.	Search Engine Optimization (SEO): Optimizing website content to rank higher in search engine results.
ii.	Pay-Per-Click (PPC) Advertising: Running ads on search engines (like Google Ads) or social media platforms where you pay each time your ad is clicked.
iii.	Social Media Marketing: Using social media platforms to connect with audiences, build brands, drive traffic, and generate sales.
b.	Why it's effective: Highly targetable, measurable, scalable, and offers immediate feedback on campaign performance.
c.	Famous Examples: Every major brand leverages these, from Nike's targeted ads on Instagram to Amazon's extensive use of Google Ads for product promotion.
Big brands don't pick just one; they typically employ a combination of these strategies, with Brand Marketing forming the core, and others like Content, Experiential, and Digital Marketing serving as powerful tools to execute and amplify their brand message.

Copilot:
Absolutely, Grudge—some of the most iconic brands have built their empires on bold, creative strategies that go far beyond traditional advertising. Here are a few standout examples that have left a lasting mark:
1.	Personalized Marketing – Coca-Cola’s “Share a Coke”
2.	 Coca-Cola replaced its logo with popular names on bottles, encouraging people to find and share a Coke with friends. It sparked massive social sharing and emotional connection.
3.	Emotional Branding – Dove’s “Real Beauty” Campaign
 Dove shifted the conversation around beauty by featuring real women instead of models. This emotionally resonant campaign built trust and loyalty by aligning with deeper values.
4.	User-Generated Content – Nike’s “Nike By You”
 Nike invited customers to design their own sneakers, turning fans into creators. This not only boosted engagement but also made the product feel more personal and exclusive.
5.	Immersive Experiences – Nike’s Treadmill Video Game
 To promote their Epic React shoes, Nike created a video game that users played while running on a treadmill. It was a brilliant fusion of tech, fitness, and fun.
6.	Storytelling – Airbnb’s Community-Driven Narratives
 Airbnb built its brand by sharing real stories from hosts and travelers, making the brand feel human and trustworthy.
7.	Referral Marketing – Lyft’s Growth Hack
 Lyft grew rapidly by offering free ride credits to users who referred friends. It turned customers into brand ambassadors overnight.
8.	“Glocal” Strategy – McDonald’s Localized Menus
 McDonald’s adapts its menu to local tastes—like the McArabia in the Middle East—while maintaining global consistency. This balance of global brand and local relevance is marketing gold.
Each of these strategies taps into something deeper—identity, emotion, community, or creativity. Want to explore how one of these could inspire your own branding or business ideas?


Chat GPT:
Yes! In addition to publicity and guerrilla marketing, there are several other famous, proven marketing strategies that big brands rely on. Here's a breakdown of some of the most effective and widely used strategies:

🧠 1. Emotional Branding
•	Used by: Nike, Coca-Cola, Apple
•	Concept: Focuses on creating an emotional connection between the brand and the consumer (e.g., inspiration, belonging, happiness).
•	Example: Nike’s “Just Do It” isn’t about shoes — it’s about motivation and pushing limits.

🌍 2. Lifestyle Marketing / Experience Marketing
•	Used by: Red Bull, Starbucks, GoPro
•	Concept: Sell the lifestyle or experience surrounding the product — not just the product itself.
•	Example: Red Bull promotes extreme sports and adventure, not just an energy drink.

📢 3. Influencer & Celebrity Marketing
•	Used by: Adidas, Pepsi, Calvin Klein
•	Concept: Leveraging trusted figures with large followings to promote products.
•	Example: Adidas collaborations with Kanye West (Yeezy) and Beyoncé (Ivy Park).

📦 4. Content Marketing
•	Used by: HubSpot, Apple, LEGO
•	Concept: Providing valuable or entertaining content to build trust and keep customers engaged.
•	Example: LEGO’s YouTube channel and movies — storytelling instead of just selling toys.

📈 5. Product Placement
•	Used by: Coca-Cola, BMW, Ray-Ban
•	Concept: Integrating products into movies, shows, music videos, or games.
•	Example: Ray-Ban sunglasses in Top Gun skyrocketed their sales.

🧪 6. FOMO & Scarcity Marketing
•	Used by: Supreme, Apple (product launches), Tesla (pre-orders)
•	Concept: Create urgency by limiting availability or offering exclusive drops.
•	Example: Supreme drops weekly limited items that sell out within minutes.

🔁 7. Loyalty and Rewards Programs
•	Used by: Starbucks, Amazon Prime, Sephora
•	Concept: Reward frequent buyers to keep them coming back.
•	Example: Starbucks Rewards encourages repeated purchases through stars and perks.

💬 8. Word-of-Mouth / Referral Marketing
•	Used by: Dropbox, Tesla
•	Concept: Encourage users to share the product with others, sometimes with incentives.
•	Example: Dropbox’s early growth came from giving users free storage for referrals.

🔍 9. Search Engine & Performance Marketing
•	Used by: Amazon, Airbnb, every major e-commerce brand
•	Concept: Data-driven digital ads (Google Ads, Facebook, etc.) targeting intent and demographics.

Would you like examples of how to apply one of these strategies to your own product or business idea?



How to make a successful Intro:
 
1- Don't convince people about how good your product is.. tell them how good & attractive you will be with our product

2-Don't focus on (Human change behavior) instead focus on adding a new add-on to their old school which is stylish and modern

3-Focus on selling attitude and vipes.. Focus on layer that look to be always bad-boy or iconic and convince them that your product is considered to be their missing add-on




“راس مالك مش فلوسك بل الطريقة التسويقيه الخاصه بيك”





