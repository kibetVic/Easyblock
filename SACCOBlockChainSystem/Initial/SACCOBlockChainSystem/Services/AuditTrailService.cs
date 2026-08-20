using Microsoft.EntityFrameworkCore;
using SACCOBlockChainSystem.Data;
using SACCOBlockChainSystem.Models;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace SACCOBlockChainSystem.Services
{
    public enum AuditActionType
    {
        Insert,
        Update,
        Delete,
        View,
        Login,
        Logout,
        Export
    }

    public class AuditTrailService
    {
        private readonly ApplicationDbContext _context;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<AuditTrailService> _logger;

        public AuditTrailService(
            ApplicationDbContext context,
            IHttpContextAccessor httpContextAccessor,
            ILogger<AuditTrailService> logger)
        {
            _context = context;
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
        }

        public async Task SaveLogAsync(
            AuditActionType actionType,
            object? oldModel = null,
            object? newModel = null,
            string? tableName = null,
            string? recordId = null,
            string? userId = null,
            string? userName = null,
            string? companyCode = null,
            string? module = null,
            string? correlationId = null,
            string? blockchainTxId = null,
            string? extraData = null)
        {
            try
            {
                var httpContext = _httpContextAccessor.HttpContext;

                // AUTO-DETECT EVERYTHING FROM THE REQUEST
                var ipAddress = GetClientIpAddress(httpContext);
                var hostName = GetClientHostName(httpContext, ipAddress);
                var location = GetLocationFromIp(ipAddress);
                var browserAgent = GetUserAgent(httpContext);
                var machineName = GetMachineName();

                var audit = new AuditTrail
                {
                    AuditTime = DateTime.Now,
                    ActionType = actionType.ToString(),
                    TableName = tableName,
                    RecordId = recordId,
                    UserId = userId ?? GetCurrentUserId(httpContext) ?? "SYSTEM",
                    UserName = userName ?? GetCurrentUserName(httpContext) ?? "SYSTEM",
                    CompanyCode = companyCode,
                    IpAddress = ipAddress,
                    HostName = hostName,
                    Location = location,
                    BrowserAgent = browserAgent,
                    Module = module,
                    CorrelationId = correlationId ?? Guid.NewGuid().ToString(),
                    BlockchainTxId = blockchainTxId,
                    ExtraData = extraData
                };

                // Serialize old/new values
                switch (actionType)
                {
                    case AuditActionType.Insert:
                        audit.ActionDescription = $"Record inserted into {tableName}";
                        audit.NewValue = newModel != null ? JsonSerializer.Serialize(newModel, new JsonSerializerOptions { WriteIndented = false }) : null;
                        break;

                    case AuditActionType.Update:
                        audit.ActionDescription = $"Record updated in {tableName}";
                        audit.OldValue = oldModel != null ? JsonSerializer.Serialize(oldModel, new JsonSerializerOptions { WriteIndented = false }) : null;
                        audit.NewValue = newModel != null ? JsonSerializer.Serialize(newModel, new JsonSerializerOptions { WriteIndented = false }) : null;
                        break;

                    case AuditActionType.Delete:
                        audit.ActionDescription = $"Record deleted from {tableName}";
                        audit.OldValue = oldModel != null ? JsonSerializer.Serialize(oldModel, new JsonSerializerOptions { WriteIndented = false }) : null;
                        break;

                    case AuditActionType.View:
                        audit.ActionDescription = $"Record viewed from {tableName}";
                        break;

                    case AuditActionType.Login:
                        audit.ActionDescription = $"User login attempt";
                        break;

                    case AuditActionType.Logout:
                        audit.ActionDescription = $"User logout";
                        break;
                }

                await _context.AuditTrails.AddAsync(audit);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"Audit saved: {actionType} on {tableName} by {audit.UserName} | IP: {ipAddress} | Host: {hostName} | Location: {location}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving audit log");
                // Don't throw - audit shouldn't break main functionality
            }
        }

        private string GetClientIpAddress(HttpContext? httpContext)
        {
            try
            {
                if (httpContext == null)
                {
                    _logger.LogWarning("HttpContext is null, using local IP");
                    return GetLocalIPAddress();
                }

                // Try multiple sources for real client IP
                string? ipAddress = null;

                // 1. Check CloudFlare header
                ipAddress = httpContext.Request.Headers["CF-Connecting-IP"].FirstOrDefault();
                if (!string.IsNullOrEmpty(ipAddress)) return ipAddress;

                // 2. Check X-Forwarded-For (proxy/load balancer)
                ipAddress = httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
                if (!string.IsNullOrEmpty(ipAddress))
                {
                    // Get first IP if multiple
                    return ipAddress.Split(',').First().Trim();
                }

                // 3. Check X-Real-IP
                ipAddress = httpContext.Request.Headers["X-Real-IP"].FirstOrDefault();
                if (!string.IsNullOrEmpty(ipAddress)) return ipAddress;

                // 4. Get from remote connection
                ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();
                if (!string.IsNullOrEmpty(ipAddress) && ipAddress != "::1" && ipAddress != "127.0.0.1")
                {
                    return ipAddress;
                }

                // 5. Fallback to local IP
                return GetLocalIPAddress();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting client IP");
                return "Unknown";
            }
        }

        private string GetClientHostName(HttpContext? httpContext, string ipAddress)
        {
            try
            {
                // First try to get host from request
                if (httpContext != null)
                {
                    var requestHost = httpContext.Request.Host.Value;
                    if (!string.IsNullOrEmpty(requestHost))
                    {
                        return requestHost;
                    }
                }

                // If not found, try to resolve from IP address
                if (!string.IsNullOrEmpty(ipAddress) && ipAddress != "Unknown")
                {
                    try
                    {
                        var hostEntry = Dns.GetHostEntry(ipAddress);
                        if (!string.IsNullOrEmpty(hostEntry.HostName))
                        {
                            return hostEntry.HostName;
                        }
                    }
                    catch
                    {
                        // DNS resolution failed
                    }
                }

                // Fallback to machine name
                return Environment.MachineName;
            }
            catch
            {
                return "Unknown";
            }
        }

        private string GetLocationFromIp(string ipAddress)
        {
            try
            {
                if (string.IsNullOrEmpty(ipAddress) || ipAddress == "Unknown")
                    return "Unknown";

                // Local addresses
                if (ipAddress == "127.0.0.1" || ipAddress == "::1")
                    return "Local Host";

                // Private network ranges
                if (ipAddress.StartsWith("192.168.") ||
                    ipAddress.StartsWith("10.") ||
                    ipAddress.StartsWith("172.16.") ||
                    ipAddress.StartsWith("172.17.") ||
                    ipAddress.StartsWith("172.18.") ||
                    ipAddress.StartsWith("172.19.") ||
                    ipAddress.StartsWith("172.20.") ||
                    ipAddress.StartsWith("172.21.") ||
                    ipAddress.StartsWith("172.22.") ||
                    ipAddress.StartsWith("172.23.") ||
                    ipAddress.StartsWith("172.24.") ||
                    ipAddress.StartsWith("172.25.") ||
                    ipAddress.StartsWith("172.26.") ||
                    ipAddress.StartsWith("172.27.") ||
                    ipAddress.StartsWith("172.28.") ||
                    ipAddress.StartsWith("172.29.") ||
                    ipAddress.StartsWith("172.30.") ||
                    ipAddress.StartsWith("172.31."))
                {
                    return "Local Network";
                }

                // You can integrate with a free GeoIP API here
                // For now, return based on IP patterns
                // This is where you'd call an API like ipapi.co or ip-api.com

                // Example: Return country based on IP ranges
                if (ipAddress.StartsWith("197.248.")) return "Kenya";
                if (ipAddress.StartsWith("105.")) return "Kenya";
                if (ipAddress.StartsWith("41.")) return "Kenya";
                if (ipAddress.StartsWith("154.")) return "Kenya";

                // Default
                return "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }

        private string GetUserAgent(HttpContext? httpContext)
        {
            try
            {
                if (httpContext == null) return "Unknown";
                return httpContext.Request.Headers["User-Agent"].ToString() ?? "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }

        private string GetMachineName()
        {
            try
            {
                return Environment.MachineName;
            }
            catch
            {
                return "Unknown";
            }
        }

        private string GetLocalIPAddress()
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                    {
                        return ip.ToString();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting local IP address");
            }
            return "127.0.0.1";
        }

        private string GetCurrentUserId(HttpContext? httpContext)
        {
            try
            {
                if (httpContext?.User == null) return null;
                return httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            }
            catch
            {
                return null;
            }
        }

        private string GetCurrentUserName(HttpContext? httpContext)
        {
            try
            {
                if (httpContext?.User == null) return null;
                return httpContext.User.Identity?.Name ??
                       httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;
            }
            catch
            {
                return null;
            }
        }
    }
}