// Services/SmsService.cs
using Microsoft.EntityFrameworkCore;
using SACCOBlockChainSystem.Data;
using SACCOBlockChainSystem.Models;
using SACCOBlockChainSystem.Models.DTOs;
using System.Text;
using System.Text.Json;

namespace SACCOBlockChainSystem.Services
{
    public interface ISmsService
    {
        // SMS Management
        Task<SmsResponseDTO> SendSmsAsync(SendSmsRequestDTO request);
        Task<SmsResponseDTO> SendTemplateSmsAsync(string templateCode, string phoneNumber, string recipientName, Dictionary<string, string> parameters, string reference = null);
        Task<List<SmsResponseDTO>> SendBulkSmsAsync(BulkSmsRequestDTO request);

        // Retrieval
        Task<SmsResponseDTO> GetSmsByIdAsync(int id);
        Task<SmsResponseDTO> GetSmsByMessageIdAsync(string messageId);
        Task<List<SmsResponseDTO>> GetSmsByPhoneNumberAsync(string phoneNumber, int page = 1, int pageSize = 50);
        Task<List<SmsResponseDTO>> GetSmsByReferenceAsync(string reference, int page = 1, int pageSize = 50);
        Task<List<SmsResponseDTO>> GetSmsByStatusAsync(string status, int page = 1, int pageSize = 50);
        Task<SmsStatisticsDTO> GetSmsStatisticsAsync(DateTime? fromDate = null, DateTime? toDate = null);

        // Templates
        Task<SmsTemplate> CreateTemplateAsync(SmsTemplateDTO dto, string createdBy);
        Task<SmsTemplate> UpdateTemplateAsync(int id, SmsTemplateDTO dto, string modifiedBy);
        Task<bool> DeleteTemplateAsync(int id);
        Task<List<SmsTemplate>> GetAllTemplatesAsync(string companyCode);
        Task<SmsTemplate> GetTemplateByCodeAsync(string templateCode, string companyCode);

        // Settings
        Task<SmsSetting> GetSmsSettingsAsync(string companyCode);
        Task<SmsSetting> UpdateSmsSettingsAsync(string companyCode, SmsSettingDTO dto, string updatedBy);

        // Webhook for delivery reports
        Task<bool> UpdateDeliveryStatusAsync(string providerMessageId, string status, string errorMessage = null);

        // Blockchain
        Task<bool> RecordSmsOnBlockchainAsync(int smsId);
    }

    public class SmsService : ISmsService
    {
        private readonly ApplicationDbContext _context;
        private readonly IBlockchainService _blockchainService;
        private readonly ICompanyContextService _companyContextService;
        private readonly ILogger<SmsService> _logger;
        private readonly HttpClient _httpClient;

        public SmsService(
            ApplicationDbContext context,
            IBlockchainService blockchainService,
            ICompanyContextService companyContextService,
            ILogger<SmsService> logger,
            IHttpClientFactory httpClientFactory)
        {
            _context = context;
            _blockchainService = blockchainService;
            _companyContextService = companyContextService;
            _logger = logger;
            _httpClient = httpClientFactory.CreateClient();
        }

        public async Task<SmsResponseDTO> SendSmsAsync(SendSmsRequestDTO request)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var companyCode = _companyContextService.GetCurrentCompanyCode();
                var settings = await GetSmsSettingsAsync(companyCode);

                if (!settings.IsEnabled)
                {
                    throw new InvalidOperationException("SMS service is disabled for this company");
                }

                // Generate unique message ID
                var messageId = GenerateMessageId();

                // Create SMS record
                var sms = new SmsMessage
                {
                    MessageId = messageId,
                    PhoneNumber = request.PhoneNumber,
                    RecipientName = request.RecipientName ?? request.PhoneNumber,
                    MessageContent = request.Message,
                    MessageType = request.MessageType ?? "General",
                    Reference = request.Reference,
                    Status = "Pending",
                    RetryCount = 0,
                    Provider = settings.Provider,
                    CompanyCode = companyCode,
                    CreatedBy = "SYSTEM",
                    CreatedAt = DateTime.Now
                };

                _context.SmsMessages.Add(sms);
                await _context.SaveChangesAsync();

                // Send SMS via provider
                bool sent = false;
                string providerMessageId = null;
                string errorMessage = null;

                try
                {
                    if (settings.Provider == "AfricaTalking")
                    {
                        providerMessageId = await SendViaAfricaTalkingAsync(settings, request.PhoneNumber, request.Message);
                        sent = true;
                    }
                    else if (settings.Provider == "Twilio")
                    {
                        providerMessageId = await SendViaTwilioAsync(settings, request.PhoneNumber, request.Message);
                        sent = true;
                    }
                    else
                    {
                        // For testing/development - simulate sending
                        providerMessageId = $"SIM-{DateTime.Now.Ticks}";
                        sent = true;
                        _logger.LogInformation($"SIMULATED SMS to {request.PhoneNumber}: {request.Message}");
                    }

                    if (sent)
                    {
                        sms.Status = "Sent";
                        sms.SentAt = DateTime.Now;
                        sms.ProviderMessageId = providerMessageId;
                        sms.Cost = settings.CostPerSms;
                    }
                }
                catch (Exception ex)
                {
                    errorMessage = ex.Message;
                    sms.Status = "Failed";
                    sms.ErrorMessage = errorMessage;
                    _logger.LogError(ex, $"Failed to send SMS to {request.PhoneNumber}");
                }

                await _context.SaveChangesAsync();

                // Record on blockchain
                await RecordSmsOnBlockchainAsync(sms.Id);

                await transaction.CommitAsync();

                return MapToResponseDTO(sms);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error sending SMS");
                throw;
            }
        }

        public async Task<SmsResponseDTO> SendTemplateSmsAsync(
            string templateCode,
            string phoneNumber,
            string recipientName,
            Dictionary<string, string> parameters,
            string reference = null)
        {
            var companyCode = _companyContextService.GetCurrentCompanyCode();
            var template = await GetTemplateByCodeAsync(templateCode, companyCode);

            if (template == null)
            {
                throw new InvalidOperationException($"SMS template '{templateCode}' not found");
            }

            if (!template.IsActive)
            {
                throw new InvalidOperationException($"SMS template '{templateCode}' is inactive");
            }

            // Replace placeholders in template
            var message = template.TemplateContent;
            foreach (var param in parameters)
            {
                message = message.Replace($"{{{{{param.Key}}}}}", param.Value);
            }

            var request = new SendSmsRequestDTO
            {
                PhoneNumber = phoneNumber,
                RecipientName = recipientName,
                Message = message,
                MessageType = template.TemplateName,
                Reference = reference
            };

            return await SendSmsAsync(request);
        }

        public async Task<List<SmsResponseDTO>> SendBulkSmsAsync(BulkSmsRequestDTO request)
        {
            var results = new List<SmsResponseDTO>();
            var errors = new List<string>();

            foreach (var msg in request.Messages)
            {
                try
                {
                    var result = await SendSmsAsync(msg);
                    results.Add(result);
                }
                catch (Exception ex)
                {
                    errors.Add($"Failed to send to {msg.PhoneNumber}: {ex.Message}");
                    _logger.LogError(ex, $"Bulk SMS failed for {msg.PhoneNumber}");
                }
            }

            if (errors.Any())
            {
                _logger.LogWarning($"Bulk SMS completed with {errors.Count} errors: {string.Join("; ", errors)}");
            }

            return results;
        }

        public async Task<SmsResponseDTO> GetSmsByIdAsync(int id)
        {
            var sms = await _context.SmsMessages.FindAsync(id);
            return sms != null ? MapToResponseDTO(sms) : null;
        }

        public async Task<SmsResponseDTO> GetSmsByMessageIdAsync(string messageId)
        {
            var sms = await _context.SmsMessages
                .FirstOrDefaultAsync(s => s.MessageId == messageId);
            return sms != null ? MapToResponseDTO(sms) : null;
        }

        public async Task<List<SmsResponseDTO>> GetSmsByPhoneNumberAsync(string phoneNumber, int page = 1, int pageSize = 50)
        {
            var sms = await _context.SmsMessages
                .Where(s => s.PhoneNumber == phoneNumber)
                .OrderByDescending(s => s.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return sms.Select(MapToResponseDTO).ToList();
        }

        public async Task<List<SmsResponseDTO>> GetSmsByReferenceAsync(string reference, int page = 1, int pageSize = 50)
        {
            var sms = await _context.SmsMessages
                .Where(s => s.Reference == reference)
                .OrderByDescending(s => s.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return sms.Select(MapToResponseDTO).ToList();
        }

        public async Task<List<SmsResponseDTO>> GetSmsByStatusAsync(string status, int page = 1, int pageSize = 50)
        {
            var sms = await _context.SmsMessages
                .Where(s => s.Status == status)
                .OrderByDescending(s => s.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return sms.Select(MapToResponseDTO).ToList();
        }

        public async Task<SmsStatisticsDTO> GetSmsStatisticsAsync(DateTime? fromDate = null, DateTime? toDate = null)
        {
            var companyCode = _companyContextService.GetCurrentCompanyCode();
            var query = _context.SmsMessages
                .Where(s => s.CompanyCode == companyCode);

            if (fromDate.HasValue)
            {
                query = query.Where(s => s.CreatedAt >= fromDate.Value);
            }
            if (toDate.HasValue)
            {
                query = query.Where(s => s.CreatedAt <= toDate.Value);
            }

            var messages = await query.ToListAsync();

            var statistics = new SmsStatisticsDTO
            {
                TotalSent = messages.Count(s => s.Status == "Sent"),
                TotalPending = messages.Count(s => s.Status == "Pending"),
                TotalFailed = messages.Count(s => s.Status == "Failed"),
                TotalDelivered = messages.Count(s => s.Status == "Delivered"),
                TotalCost = messages.Sum(s => s.Cost ?? 0),
                ByType = messages.GroupBy(s => s.MessageType)
                    .ToDictionary(g => g.Key ?? "Unknown", g => g.Count()),
                ByStatus = messages.GroupBy(s => s.Status)
                    .ToDictionary(g => g.Key, g => g.Count()),
                RecentMessages = messages
                    .OrderByDescending(s => s.CreatedAt)
                    .Take(20)
                    .Select(MapToResponseDTO)
                    .ToList()
            };

            return statistics;
        }

        public async Task<SmsTemplate> CreateTemplateAsync(SmsTemplateDTO dto, string createdBy)
        {
            var companyCode = _companyContextService.GetCurrentCompanyCode();

            var template = new SmsTemplate
            {
                TemplateCode = dto.TemplateCode,
                TemplateName = dto.TemplateName,
                TemplateContent = dto.TemplateContent,
                Description = dto.Description,
                IsActive = dto.IsActive,
                CompanyCode = companyCode,
                CreatedBy = createdBy,
                CreatedAt = DateTime.Now
            };

            _context.SmsTemplates.Add(template);
            await _context.SaveChangesAsync();

            _logger.LogInformation($"SMS template created: {template.TemplateCode}");
            return template;
        }

        public async Task<SmsTemplate> UpdateTemplateAsync(int id, SmsTemplateDTO dto, string modifiedBy)
        {
            var template = await _context.SmsTemplates.FindAsync(id);
            if (template == null)
            {
                throw new InvalidOperationException($"Template with ID {id} not found");
            }

            template.TemplateCode = dto.TemplateCode;
            template.TemplateName = dto.TemplateName;
            template.TemplateContent = dto.TemplateContent;
            template.Description = dto.Description;
            template.IsActive = dto.IsActive;
            template.ModifiedBy = modifiedBy;
            template.ModifiedAt = DateTime.Now;

            await _context.SaveChangesAsync();

            _logger.LogInformation($"SMS template updated: {template.TemplateCode}");
            return template;
        }

        public async Task<bool> DeleteTemplateAsync(int id)
        {
            var template = await _context.SmsTemplates.FindAsync(id);
            if (template == null)
            {
                return false;
            }

            _context.SmsTemplates.Remove(template);
            await _context.SaveChangesAsync();

            _logger.LogInformation($"SMS template deleted: {template.TemplateCode}");
            return true;
        }

        public async Task<List<SmsTemplate>> GetAllTemplatesAsync(string companyCode)
        {
            return await _context.SmsTemplates
                .Where(t => t.CompanyCode == companyCode)
                .OrderBy(t => t.TemplateName)
                .ToListAsync();
        }

        public async Task<SmsTemplate> GetTemplateByCodeAsync(string templateCode, string companyCode)
        {
            return await _context.SmsTemplates
                .FirstOrDefaultAsync(t => t.TemplateCode == templateCode && t.CompanyCode == companyCode);
        }

        // Services/SmsService.cs - Update these methods

        public async Task<SmsSetting> GetSmsSettingsAsync(string companyCode)
        {
            var settings = await _context.SmsSettings
                .FirstOrDefaultAsync(s => s.CompanyCode == companyCode);

            if (settings == null)
            {
                // Get company name from SaccoParram
                var company = await _context.SaccoParram
                    .FirstOrDefaultAsync(s => s.CompanyCode == companyCode);

                var companyName = company?.SaccoName ?? "JUHUDI SACCO";

                // Clean company name for sender ID (max 11 chars, uppercase, no spaces)
                var senderId = CleanSenderId(companyName);

                // Create default settings with all fields
                settings = new SmsSetting
                {
                    CompanyCode = companyCode,
                    Provider = "AfricaTalking",
                    IsEnabled = true,
                    SendOnRegistration = true,
                    SendOnWithdrawal = true,
                    SendOnLoanApproval = true,
                    SendOnShareTransfer = true,
                    SendOnContribution = true,
                    SendOnLoanRepayment = true,
                    SendOnAGM = true,
                    SendOnDeposits = true,
                    CostPerSms = 0.50m,
                    SenderId = senderId,
                    ApiKey = null,
                    ApiSecret = null,
                    Username = null,
                    ShortCode = null,
                    ApiEndpoint = "https://api.africastalking.com/version1/messaging",
                    CreatedAt = DateTime.Now,
                    UpdatedAt = null,
                    UpdatedBy = null,
                    BlockchainTxId = null
                };

                _context.SmsSettings.Add(settings);
                await _context.SaveChangesAsync();
            }

            return settings;
        }

        public async Task<SmsSetting> UpdateSmsSettingsAsync(string companyCode, SmsSettingDTO dto, string updatedBy)
        {
            var settings = await GetSmsSettingsAsync(companyCode);

            // Update all fields from DTO
            settings.Provider = dto.Provider;
            settings.ApiKey = dto.ApiKey;
            settings.ApiSecret = dto.ApiSecret;
            settings.SenderId = dto.SenderId;
            settings.Username = dto.Username;
            settings.ShortCode = dto.ShortCode;
            settings.IsEnabled = dto.IsEnabled;
            settings.SendOnRegistration = dto.SendOnRegistration;
            settings.SendOnWithdrawal = dto.SendOnWithdrawal;
            settings.SendOnLoanApproval = dto.SendOnLoanApproval;
            settings.SendOnShareTransfer = dto.SendOnShareTransfer;
            settings.SendOnContribution = dto.SendOnContribution;
            settings.SendOnLoanRepayment = dto.SendOnLoanRepayment;
            settings.SendOnAGM = dto.SendOnAGM;
            settings.SendOnDeposits = dto.SendOnDeposits;
            settings.CostPerSms = dto.CostPerSms;
            settings.ApiEndpoint = dto.ApiEndpoint;
            settings.UpdatedAt = DateTime.Now;
            settings.UpdatedBy = updatedBy;

            await _context.SaveChangesAsync();

            // Record blockchain transaction for settings update
            await RecordSettingsOnBlockchainAsync(settings, updatedBy);

            _logger.LogInformation($"SMS settings updated for company {companyCode}");
            return settings;
        }

        private async Task RecordSettingsOnBlockchainAsync(SmsSetting settings, string updatedBy)
        {
            try
            {
                var blockchainData = new
                {
                    settings.Id,
                    settings.CompanyCode,
                    settings.Provider,
                    settings.IsEnabled,
                    settings.SendOnRegistration,
                    settings.SendOnWithdrawal,
                    settings.SendOnLoanApproval,
                    settings.SendOnShareTransfer,
                    settings.SendOnContribution,
                    settings.SendOnLoanRepayment,
                    settings.SendOnAGM,
                    settings.SendOnDeposits,
                    settings.CostPerSms,
                    UpdatedBy = updatedBy,
                    UpdatedAt = DateTime.Now
                };

                var blockchainTx = await _blockchainService.CreateAndAddTransactionAsync(
                    "SMS_SETTINGS_UPDATED",
                    settings.CompanyCode,
                    settings.CompanyCode,
                    settings.CostPerSms,
                    $"SMS-SETTINGS-{settings.Id}",
                    blockchainData);

                if (blockchainTx != null)
                {
                    settings.BlockchainTxId = blockchainTx.TransactionId;
                    await _context.SaveChangesAsync();
                    _logger.LogInformation($"SMS settings recorded on blockchain: {blockchainTx.TransactionId}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to record SMS settings on blockchain");
            }
        }

        private string CleanSenderId(string companyName)
        {
            if (string.IsNullOrEmpty(companyName))
                return "SACCO";

            // Remove any special characters, convert to uppercase
            var cleaned = new string(companyName.Where(c => char.IsLetterOrDigit(c)).ToArray());

            // Take first 11 characters (SMS sender ID limit)
            if (cleaned.Length > 11)
            {
                cleaned = cleaned.Substring(0, 11);
            }

            return cleaned.ToUpper();
        }
        public async Task<bool> UpdateDeliveryStatusAsync(string providerMessageId, string status, string errorMessage = null)
        {
            var sms = await _context.SmsMessages
                .FirstOrDefaultAsync(s => s.ProviderMessageId == providerMessageId);

            if (sms == null)
            {
                _logger.LogWarning($"SMS not found for provider message ID: {providerMessageId}");
                return false;
            }

            sms.Status = status;
            if (status == "Delivered")
            {
                sms.DeliveredAt = DateTime.Now;
            }
            if (!string.IsNullOrEmpty(errorMessage))
            {
                sms.ErrorMessage = errorMessage;
            }

            await _context.SaveChangesAsync();

            _logger.LogInformation($"SMS delivery updated: {providerMessageId} -> {status}");
            return true;
        }

        public async Task<bool> RecordSmsOnBlockchainAsync(int smsId)
        {
            try
            {
                var sms = await _context.SmsMessages.FindAsync(smsId);
                if (sms == null) return false;

                var blockchainData = new
                {
                    sms.Id,
                    sms.MessageId,
                    sms.PhoneNumber,
                    sms.RecipientName,
                    sms.MessageType,
                    sms.Reference,
                    sms.Status,
                    sms.Provider,
                    sms.Cost,
                    sms.SentAt,
                    sms.DeliveredAt,
                    sms.CreatedAt
                };

                var blockchainTx = await _blockchainService.CreateAndAddTransactionAsync(
                    $"SMS_{sms.Status.ToUpper()}",
                    sms.PhoneNumber,
                    sms.CompanyCode,
                    sms.Cost ?? 0,
                    sms.MessageId,
                    blockchainData);

                if (blockchainTx != null)
                {
                    sms.BlockchainTxId = blockchainTx.TransactionId;
                    await _context.SaveChangesAsync();
                    _logger.LogInformation($"SMS recorded on blockchain: {blockchainTx.TransactionId}");
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error recording SMS on blockchain");
                return false;
            }
        }

        private async Task<string> SendViaAfricaTalkingAsync(SmsSetting settings, string phoneNumber, string message)
        {
            // Implementation for Africa's Talking API
            // This is a placeholder - implement actual API call
            var apiKey = settings.ApiKey;
            var username = settings.Username;
            var senderId = settings.SenderId;

            // Example API call - adjust based on actual API
            var payload = new
            {
                username = username,
                to = phoneNumber,
                message = message,
                from = senderId
            };

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            _httpClient.DefaultRequestHeaders.Add("apiKey", apiKey);

            var response = await _httpClient.PostAsync(settings.ApiEndpoint ?? "https://api.africastalking.com/version1/messaging", content);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Africa's Talking API error: {responseContent}");
            }

            // Parse response to get message ID
            var responseData = JsonSerializer.Deserialize<JsonElement>(responseContent);
            return responseData.GetProperty("SMSMessageData").GetProperty("Recipients")[0].GetProperty("messageId").GetString();
        }

        private async Task<string> SendViaTwilioAsync(SmsSetting settings, string phoneNumber, string message)
        {
            // Implementation for Twilio API
            // This is a placeholder - implement actual API call
            var accountSid = settings.ApiKey;
            var authToken = settings.ApiSecret;
            var fromNumber = settings.ShortCode;

            // Example API call - adjust based on actual API
            var payload = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("To", phoneNumber),
                new KeyValuePair<string, string>("From", fromNumber),
                new KeyValuePair<string, string>("Body", message)
            });

            _httpClient.DefaultRequestHeaders.Add("Authorization",
                $"Basic {Convert.ToBase64String(Encoding.ASCII.GetBytes($"{accountSid}:{authToken}"))}");

            var response = await _httpClient.PostAsync($"https://api.twilio.com/2010-04-01/Accounts/{accountSid}/Messages.json", payload);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Twilio API error: {responseContent}");
            }

            var responseData = JsonSerializer.Deserialize<JsonElement>(responseContent);
            return responseData.GetProperty("sid").GetString();
        }

        private string GenerateMessageId()
        {
            return $"MSG-{DateTime.Now:yyyyMMddHHmmss}-{Guid.NewGuid().ToString().Substring(0, 8)}";
        }

        private SmsResponseDTO MapToResponseDTO(SmsMessage sms)
        {
            return new SmsResponseDTO
            {
                Id = sms.Id,
                MessageId = sms.MessageId,
                PhoneNumber = sms.PhoneNumber,
                RecipientName = sms.RecipientName,
                MessageContent = sms.MessageContent,
                MessageType = sms.MessageType,
                Reference = sms.Reference,
                Status = sms.Status,
                ErrorMessage = sms.ErrorMessage,
                SentAt = sms.SentAt,
                DeliveredAt = sms.DeliveredAt,
                RetryCount = sms.RetryCount,
                Provider = sms.Provider,
                ProviderMessageId = sms.ProviderMessageId,
                Cost = sms.Cost,
                CreatedAt = sms.CreatedAt,
                BlockchainTxId = sms.BlockchainTxId
            };
        }
    }
}