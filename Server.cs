using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace SevaSetuBackend
{
    class Program
    {
        private static int port = 5000;
        private static string rootDir;
        private static string dataDir;
        private static TcpListener listener;
        private static bool isRunning = true;
        private static readonly object fileLock = new object();

        static void Main(string[] args)
        {
            int customPort;
            if (args.Length > 0 && int.TryParse(args[0], out customPort))
            {
                port = customPort;
            }

            // Determine root and data directories
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            // If running inside backend/bin, root is two levels up
            if (Directory.Exists(Path.Combine(exeDir, "..", "..", "home")))
            {
                rootDir = Path.GetFullPath(Path.Combine(exeDir, "..", ".."));
            }
            else if (Directory.Exists(Path.Combine(exeDir, "home")))
            {
                rootDir = Path.GetFullPath(exeDir);
            }
            else
            {
                rootDir = Directory.GetCurrentDirectory();
            }

            dataDir = Path.Combine(rootDir, "backend", "data");
            if (!Directory.Exists(dataDir))
            {
                Directory.CreateDirectory(dataDir);
            }

            Console.WriteLine("=================================================");
            Console.WriteLine("   SevaSetu Social Impact Network — Backend API   ");
            Console.WriteLine("=================================================");
            Console.WriteLine("Server root: " + rootDir);
            Console.WriteLine("Data folder: " + dataDir);
            Console.WriteLine("Listening on: http://localhost:" + port);
            Console.WriteLine("Press Ctrl+C to terminate server.");
            Console.WriteLine("-------------------------------------------------");

            listener = new TcpListener(IPAddress.Any, port);
            listener.Start();

            while (isRunning)
            {
                try
                {
                    TcpClient client = listener.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem(HandleClient, client);
                }
                catch (Exception ex)
                {
                    if (!isRunning) break;
                    Console.WriteLine("[Error] Listener exception: " + ex.Message);
                }
            }
        }

        private static void HandleClient(object state)
        {
            TcpClient client = (TcpClient)state;
            try
            {
                using (NetworkStream stream = client.GetStream())
                {
                    byte[] buffer = new byte[16384];
                    int bytesRead = stream.Read(buffer, 0, buffer.Length);
                    if (bytesRead <= 0) return;

                    string rawRequest = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    string[] lines = rawRequest.Split(new string[] { "\r\n" }, StringSplitOptions.None);
                    if (lines.Length == 0) return;

                    string requestLine = lines[0];
                    string[] tokens = requestLine.Split(' ');
                    if (tokens.Length < 2) return;

                    string method = tokens[0].ToUpper();
                    string fullUrl = tokens[1];

                    // Extract path and query
                    string path = fullUrl;
                    string query = "";
                    int qIdx = fullUrl.IndexOf('?');
                    if (qIdx >= 0)
                    {
                        path = fullUrl.Substring(0, qIdx);
                        query = fullUrl.Substring(qIdx + 1);
                    }

                    // Extract body if POST/PUT
                    string body = "";
                    int headerEndIdx = rawRequest.IndexOf("\r\n\r\n");
                    if (headerEndIdx >= 0)
                    {
                        body = rawRequest.Substring(headerEndIdx + 4);
                    }

                    Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + method + " " + path);

                    // Handle CORS Pre-flight
                    if (method == "OPTIONS")
                    {
                        SendResponse(stream, "200 OK", "text/plain", "");
                        return;
                    }

                    // Route API calls
                    if (path.StartsWith("/api/"))
                    {
                        HandleApiRequest(stream, method, path, query, body);
                    }
                    else
                    {
                        ServeStaticFile(stream, path);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Client Error] " + ex.Message);
            }
            finally
            {
                client.Close();
            }
        }

        private static void HandleApiRequest(NetworkStream stream, string method, string path, string query, string body)
        {
            string p = path.ToLower().TrimEnd('/');

            // GET /api/health
            if (p == "/api/health")
            {
                SendJson(stream, "{\"status\":\"online\",\"service\":\"SevaSetu Backend API\",\"version\":\"1.0.0\",\"timestamp\":\"" + DateTime.UtcNow.ToString("o") + "\"}");
                return;
            }

            // GET /api/stats
            if (p == "/api/stats")
            {
                string statsPath = Path.Combine(dataDir, "stats.json");
                string json = ReadFileSafe(statsPath, "{}");
                SendJson(stream, json);
                return;
            }

            // GET /api/organisations
            if (p == "/api/organisations" && method == "GET")
            {
                string orgsPath = Path.Combine(dataDir, "organisations.json");
                string json = ReadFileSafe(orgsPath, "[]");
                SendJson(stream, json);
                return;
            }

            // POST /api/organisations
            if (p == "/api/organisations" && method == "POST")
            {
                string orgsPath = Path.Combine(dataDir, "organisations.json");
                string existing = ReadFileSafe(orgsPath, "[]").Trim();
                if (existing.EndsWith("]")) existing = existing.Substring(0, existing.Length - 1).Trim();
                if (existing == "[") existing = "";

                string newOrg = body.Trim();
                string updated = existing.Length > 1 ? existing + ",\n  " + newOrg + "\n]" : "[\n  " + newOrg + "\n]";
                WriteFileSafe(orgsPath, updated);
                SendJson(stream, "{\"success\":true,\"message\":\"Organisation added successfully\"}");
                return;
            }

            // GET /api/donations
            if (p == "/api/donations" && method == "GET")
            {
                string donPath = Path.Combine(dataDir, "donations.json");
                string json = ReadFileSafe(donPath, "[]");
                SendJson(stream, json);
                return;
            }

            // POST /api/donations
            if (p == "/api/donations" && method == "POST")
            {
                string donPath = Path.Combine(dataDir, "donations.json");
                string existing = ReadFileSafe(donPath, "[]").Trim();
                
                int randomId = new Random().Next(1000, 9999);
                string trackingId = "TRK-SEVA-2026-" + randomId;
                string receiptId = "REC-" + randomId;
                int otp = new Random().Next(1000, 9999);

                // Inject tracking ID and OTP into new donation
                string donationJson = body.Trim();
                if (donationJson.StartsWith("{") && donationJson.EndsWith("}"))
                {
                    donationJson = donationJson.Substring(1, donationJson.Length - 2);
                }

                string fullDonation = "{\n" +
                    "    \"id\": \"" + trackingId + "\",\n" +
                    "    \"receiptId\": \"" + receiptId + "\",\n" +
                    "    \"otp\": " + otp + ",\n" +
                    "    \"currentStage\": 1,\n" +
                    "    \"status\": \"Request Created\",\n" +
                    "    \"createdAt\": \"" + DateTime.UtcNow.ToString("o") + "\",\n" +
                    donationJson + "\n" +
                    "  }";

                string updatedJson;
                if (existing == "[]" || string.IsNullOrEmpty(existing))
                {
                    updatedJson = "[\n  " + fullDonation + "\n]";
                }
                else
                {
                    string inner = existing.Substring(0, existing.LastIndexOf(']')).Trim();
                    updatedJson = inner + ",\n  " + fullDonation + "\n]";
                }

                WriteFileSafe(donPath, updatedJson);
                UpdateItemStats(1);

                SendJson(stream, "{\"success\":true,\"trackingId\":\"" + trackingId + "\",\"receiptId\":\"" + receiptId + "\",\"otp\":" + otp + ",\"donation\":" + fullDonation + "}");
                return;
            }

            // GET /api/donations/{id} or /api/tracking/{id} or POST /api/tracking/{id}/advance
            if (p.StartsWith("/api/donations/") || p.StartsWith("/api/tracking/"))
            {
                // Check for stage advancement POST /api/tracking/{id}/advance
                if (method == "POST" && p.EndsWith("/advance"))
                {
                    string actualId = p.Replace("/api/tracking/", "").Replace("/advance", "").Replace("/api/donations/", "").Trim('/');
                    int newStage = AdvanceDonationStage(actualId);
                    SendJson(stream, "{\"success\":true,\"message\":\"Stage advanced successfully\",\"id\":\"" + actualId + "\",\"currentStage\":" + newStage + "}");
                    return;
                }

                string id = p.Substring(p.LastIndexOf('/') + 1);
                string donPath = Path.Combine(dataDir, "donations.json");
                string allDonations = ReadFileSafe(donPath, "[]");
                string found = FindJsonInArray(allDonations, id);

                if (string.IsNullOrEmpty(found))
                {
                    string rescuePath = Path.Combine(dataDir, "food_rescues.json");
                    string allRescues = ReadFileSafe(rescuePath, "[]");
                    found = FindJsonInArray(allRescues, id);
                }

                if (!string.IsNullOrEmpty(found))
                {
                    SendJson(stream, found);
                }
                else
                {
                    SendResponse(stream, "404 Not Found", "application/json", "{\"error\":\"Donation not found for ID " + id + "\"}");
                }
                return;
            }

            // GET /api/food-rescues
            if (p == "/api/food-rescues" && method == "GET")
            {
                string rescuePath = Path.Combine(dataDir, "food_rescues.json");
                string json = ReadFileSafe(rescuePath, "[]");
                SendJson(stream, json);
                return;
            }

            // POST /api/food-rescues
            if (p == "/api/food-rescues" && method == "POST")
            {
                string rescuePath = Path.Combine(dataDir, "food_rescues.json");
                string existing = ReadFileSafe(rescuePath, "[]").Trim();

                int randNum = new Random().Next(1000, 9999);
                string rescueId = "RESC-2026-" + randNum;

                // Safety validation: check if unsafe packaging or storage
                if (body.Contains("\"opened\"") || body.Contains("\"ambient_old\""))
                {
                    SendResponse(stream, "400 Bad Request", "application/json", "{\"success\":false,\"error\":\"Food safety check failed: opened containers or room temperature storage > 4 hours cannot be redistributed under FSSAI zero-waste policy.\"}");
                    return;
                }

                string rescueObj = body.Trim();
                if (rescueObj.StartsWith("{") && rescueObj.EndsWith("}"))
                {
                    rescueObj = rescueObj.Substring(1, rescueObj.Length - 2);
                }

                string fullRescue = "{\n" +
                    "    \"id\": \"" + rescueId + "\",\n" +
                    "    \"safetyStatus\": \"Approved for Rescue\",\n" +
                    "    \"status\": \"Dispatched\",\n" +
                    "    \"driverAssigned\": \"Karthik Sundaram (Insulated Cargo EV)\",\n" +
                    "    \"createdAt\": \"" + DateTime.UtcNow.ToString("o") + "\",\n" +
                    rescueObj + "\n" +
                    "  }";

                string updatedJson;
                if (existing == "[]" || string.IsNullOrEmpty(existing))
                {
                    updatedJson = "[\n  " + fullRescue + "\n]";
                }
                else
                {
                    string inner = existing.Substring(0, existing.LastIndexOf(']')).Trim();
                    updatedJson = inner + ",\n  " + fullRescue + "\n]";
                }

                WriteFileSafe(rescuePath, updatedJson);
                UpdateMealStats(35);

                SendJson(stream, "{\"success\":true,\"id\":\"" + rescueId + "\",\"message\":\"Rescue partner dispatched\",\"rescue\":" + fullRescue + "}");
                return;
            }

            // GET /api/proof/{id}
            if (p.StartsWith("/api/proof/"))
            {
                string id = p.Substring(p.LastIndexOf('/') + 1);
                string donPath = Path.Combine(dataDir, "donations.json");
                string allDonations = ReadFileSafe(donPath, "[]");
                string found = FindJsonInArray(allDonations, id);

                if (!string.IsNullOrEmpty(found))
                {
                    SendJson(stream, found);
                }
                else
                {
                    SendResponse(stream, "404 Not Found", "application/json", "{\"error\":\"Proof certificate not found for ID " + id + "\"}");
                }
                return;
            }

            // POST /api/auth/login
            if (p == "/api/auth/login" && method == "POST")
            {
                SendJson(stream, "{\"success\":true,\"token\":\"seva_token_" + Guid.NewGuid().ToString("N") + "\",\"user\":{\"name\":\"Priya Venkatesh\",\"role\":\"Customer / Donor\",\"verified\":true}}");
                return;
            }

            // Fallback for unknown API
            SendResponse(stream, "404 Not Found", "application/json", "{\"error\":\"API route not found\"}");
        }

        private static int AdvanceDonationStage(string id)
        {
            string donPath = Path.Combine(dataDir, "donations.json");
            lock (fileLock)
            {
                if (!File.Exists(donPath)) return 1;
                string json = File.ReadAllText(donPath);
                // Regex advancement for currentStage
                Match m = Regex.Match(json, "\"id\"\\s*:\\s*\"" + Regex.Escape(id) + "\".*?\"currentStage\"\\s*:\\s*(\\d+)", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    int current = int.Parse(m.Groups[1].Value);
                    int next = (current % 8) + 1;
                    string replacement = Regex.Replace(m.Value, "\"currentStage\"\\s*:\\s*\\d+", "\"currentStage\": " + next);
                    json = json.Replace(m.Value, replacement);

                    string[] stageNames = new string[] {
                        "Request Created", "Pickup Confirmed", "Delivery Partner Assigned",
                        "Items Collected", "On the Way", "Reached Destination",
                        "Received by Organisation", "Donation Successfully Completed"
                    };
                    string nextStatus = stageNames[next - 1];

                    Match sm = Regex.Match(json, "\"id\"\\s*:\\s*\"" + Regex.Escape(id) + "\".*?\"status\"\\s*:\\s*\"[^\"]+\"", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                    if (sm.Success)
                    {
                        string statusReplacement = Regex.Replace(sm.Value, "\"status\"\\s*:\\s*\"[^\"]+\"", "\"status\": \"" + nextStatus + "\"");
                        json = json.Replace(sm.Value, statusReplacement);
                    }

                    File.WriteAllText(donPath, json);
                    return next;
                }
            }
            return 1;
        }

        private static void UpdateItemStats(int increment)
        {
            string statsPath = Path.Combine(dataDir, "stats.json");
            lock (fileLock)
            {
                if (File.Exists(statsPath))
                {
                    string json = File.ReadAllText(statsPath);
                    Match m = Regex.Match(json, "\"totalItemsDonated\"\\s*:\\s*(\\d+)");
                    if (m.Success)
                    {
                        int current = int.Parse(m.Groups[1].Value);
                        json = json.Replace(m.Value, "\"totalItemsDonated\": " + (current + increment));
                        File.WriteAllText(statsPath, json);
                    }
                }
            }
        }

        private static void UpdateMealStats(int portions)
        {
            string statsPath = Path.Combine(dataDir, "stats.json");
            lock (fileLock)
            {
                if (File.Exists(statsPath))
                {
                    string json = File.ReadAllText(statsPath);
                    Match m = Regex.Match(json, "\"surplusMealsRescued\"\\s*:\\s*(\\d+)");
                    if (m.Success)
                    {
                        int current = int.Parse(m.Groups[1].Value);
                        json = json.Replace(m.Value, "\"surplusMealsRescued\": " + (current + portions));
                        File.WriteAllText(statsPath, json);
                    }
                }
            }
        }

        private static string FindJsonInArray(string arrayJson, string id)
        {
            if (string.IsNullOrEmpty(arrayJson) || string.IsNullOrEmpty(id)) return null;

            int idIdx = arrayJson.IndexOf("\"" + id + "\"", StringComparison.OrdinalIgnoreCase);
            if (idIdx < 0)
            {
                idIdx = arrayJson.IndexOf(id, StringComparison.OrdinalIgnoreCase);
            }
            if (idIdx < 0) return null;

            int start = arrayJson.LastIndexOf('{', idIdx);
            if (start < 0) return null;

            int braceCount = 0;
            int end = -1;
            for (int i = start; i < arrayJson.Length; i++)
            {
                if (arrayJson[i] == '{') braceCount++;
                else if (arrayJson[i] == '}')
                {
                    braceCount--;
                    if (braceCount == 0)
                    {
                        end = i;
                        break;
                    }
                }
            }

            if (start >= 0 && end > start)
            {
                return arrayJson.Substring(start, end - start + 1);
            }
            return null;
        }

        private static void ServeStaticFile(NetworkStream stream, string urlPath)
        {
            string cleanPath = urlPath.TrimStart('/');
            if (string.IsNullOrEmpty(cleanPath)) cleanPath = "index.html";

            string filePath = Path.Combine(rootDir, cleanPath.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(filePath))
            {
                // Check if directory index
                if (Directory.Exists(filePath) && File.Exists(Path.Combine(filePath, "Home.html")))
                {
                    filePath = Path.Combine(filePath, "Home.html");
                }
                else
                {
                    SendResponse(stream, "404 Not Found", "text/html", "<h1>404 Not Found</h1><p>The requested file does not exist on SevaSetu server.</p>");
                    return;
                }
            }

            string ext = Path.GetExtension(filePath).ToLower();
            string mimeType = "application/octet-stream";
            switch (ext)
            {
                case ".html": mimeType = "text/html; charset=utf-8"; break;
                case ".css": mimeType = "text/css; charset=utf-8"; break;
                case ".js": mimeType = "application/javascript; charset=utf-8"; break;
                case ".json": mimeType = "application/json; charset=utf-8"; break;
                case ".png": mimeType = "image/png"; break;
                case ".jpg":
                case ".jpeg": mimeType = "image/jpeg"; break;
                case ".svg": mimeType = "image/svg+xml"; break;
                case ".ico": mimeType = "image/x-icon"; break;
            }

            byte[] fileBytes = File.ReadAllBytes(filePath);
            SendRawResponse(stream, "200 OK", mimeType, fileBytes);
        }

        private static void SendJson(NetworkStream stream, string json)
        {
            SendResponse(stream, "200 OK", "application/json; charset=utf-8", json);
        }

        private static void SendResponse(NetworkStream stream, string status, string contentType, string content)
        {
            byte[] bodyBytes = Encoding.UTF8.GetBytes(content);
            SendRawResponse(stream, status, contentType, bodyBytes);
        }

        private static void SendRawResponse(NetworkStream stream, string status, string contentType, byte[] bodyBytes)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("HTTP/1.1 " + status + "\r\n");
            sb.Append("Content-Type: " + contentType + "\r\n");
            sb.Append("Content-Length: " + bodyBytes.Length + "\r\n");
            sb.Append("Access-Control-Allow-Origin: *\r\n");
            sb.Append("Access-Control-Allow-Methods: GET, POST, PUT, DELETE, OPTIONS\r\n");
            sb.Append("Access-Control-Allow-Headers: Content-Type, Authorization\r\n");
            sb.Append("Connection: close\r\n\r\n");

            byte[] headerBytes = Encoding.ASCII.GetBytes(sb.ToString());
            stream.Write(headerBytes, 0, headerBytes.Length);
            if (bodyBytes.Length > 0)
            {
                stream.Write(bodyBytes, 0, bodyBytes.Length);
            }
            stream.Flush();
        }

        private static string ReadFileSafe(string path, string fallback)
        {
            lock (fileLock)
            {
                if (File.Exists(path))
                {
                    return File.ReadAllText(path, Encoding.UTF8);
                }
                return fallback;
            }
        }

        private static void WriteFileSafe(string path, string content)
        {
            lock (fileLock)
            {
                File.WriteAllText(path, content, Encoding.UTF8);
            }
        }
    }
}
