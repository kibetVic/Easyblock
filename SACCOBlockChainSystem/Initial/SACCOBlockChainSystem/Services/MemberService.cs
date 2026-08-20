using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SACCOBlockChainSystem.Data;
using SACCOBlockChainSystem.Models;
using SACCOBlockChainSystem.Models.DTOs;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace SACCOBlockChainSystem.Services
{
    public class MemberService : IMemberService
    {
        private readonly ApplicationDbContext _context;
        private readonly IBlockchainService _blockchainService;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<MemberService> _logger;
        private readonly ICompanyContextService _companyContextService;
        private readonly IHttpContextAccessor _httpContextAccesso;
        private readonly AuditTrailService _auditService;
        // private readonly UserManager<IdentityUser> _userManager;

        public MemberService(
            ApplicationDbContext context,
            IBlockchainService blockchainService,
            ILogger<MemberService> logger,
            IHttpContextAccessor httpContextAccessor,
            AuditTrailService auditService,
            //UserManager<IdentityUser> userManager,
            ICompanyContextService companyContextService)
        {
            _context = context;
            _blockchainService = blockchainService;
            _httpContextAccessor = httpContextAccessor;
            _auditService = auditService;
            _logger = logger;
            //_userManager = userManager;
            _companyContextService = companyContextService;
        }

        public string GetCurrentCompanyCode()
        {
            try
            {
                var user = _httpContextAccessor.HttpContext?.User;
                if (user == null || !user.Identity.IsAuthenticated)
                {
                    _logger.LogWarning("No authenticated user found");
                    return null;
                }

                // Get company code from claims only
                var companyCodeClaim = user.FindFirst("CompanyCode")?.Value;
                if (!string.IsNullOrEmpty(companyCodeClaim))
                {
                    _logger.LogInformation($"Company code from claim: '{companyCodeClaim}'");
                    return companyCodeClaim.Trim();
                }

                _logger.LogWarning("No company code claim found for user");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting current company code");
                return null;
            }
        }

        public string GetCurrentUserName()
        {
            return _httpContextAccessor.HttpContext?.User?.Identity?.Name;
        }

        public ClaimsPrincipal GetCurrentUserPrincipal()
        {
            return _httpContextAccessor.HttpContext?.User;
        }

        public async Task<MemberResponseDTO> RegisterMemberAsync(MemberRegistrationDTO registration)
        {
            _logger.LogInformation($"Starting member registration for: {registration.Surname} {registration.OtherNames}");

            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // Validate required fields
                if (string.IsNullOrEmpty(registration.Surname) || string.IsNullOrEmpty(registration.OtherNames))
                {
                    throw new ValidationException("Surname and Other Names are required.");
                }

                if (string.IsNullOrEmpty(registration.IdNo))
                {
                    throw new ValidationException("ID Number is required.");
                }

                // Get current user's company code
                var currentCompanyCode = _companyContextService.GetCurrentCompanyCode();
                _logger.LogInformation($"Using company code from current user context: {currentCompanyCode}");

                // Check for duplicate ID number within the same company
                var existingById = await _context.Members
                    .FirstOrDefaultAsync(m => m.Idno == registration.IdNo && m.CompanyCode == currentCompanyCode);

                if (existingById != null)
                {
                    throw new InvalidOperationException($"A member with ID number {registration.IdNo} already exists in company {currentCompanyCode}.");
                }

                // Check for duplicate phone number if provided
                if (!string.IsNullOrEmpty(registration.PhoneNo))
                {
                    var existingByPhone = await _context.Members
                        .FirstOrDefaultAsync(m => m.PhoneNo == registration.PhoneNo && m.CompanyCode == currentCompanyCode);

                    if (existingByPhone != null)
                    {
                        throw new InvalidOperationException($"Phone number {registration.PhoneNo} is already registered to another member.");
                    }
                }

                // Check for duplicate email if provided
                if (!string.IsNullOrEmpty(registration.Email))
                {
                    var existingByEmail = await _context.Members
                        .FirstOrDefaultAsync(m => m.Email == registration.Email && m.CompanyCode == currentCompanyCode);

                    if (existingByEmail != null)
                    {
                        throw new InvalidOperationException($"Email {registration.Email} is already registered to another member.");
                    }
                }

                // Determine member number - PRIORITIZE the one from the DTO
                string memberNo;

                // FIRST: Check if user provided a member number via the DTO (from the view)
                if (!string.IsNullOrEmpty(registration.MemberNo))
                {
                    // Check if the provided member number is available
                    var existingByMemberNo = await _context.Members
                        .FirstOrDefaultAsync(m => m.MemberNo == registration.MemberNo && m.CompanyCode == currentCompanyCode);

                    if (existingByMemberNo == null)
                    {
                        // Use the member number from the view/DTO
                        memberNo = registration.MemberNo;
                        _logger.LogInformation($"Using member number from view: {memberNo}");
                    }
                    else
                    {
                        // The number from view already exists - generate a new one
                        _logger.LogWarning($"Member number from view ({registration.MemberNo}) already exists. Generating new number.");
                        memberNo = await GenerateUniqueMemberNumberAsync(currentCompanyCode, registration);
                    }
                }
                else
                {
                    // No member number provided - auto-generate
                    _logger.LogInformation("No member number provided in DTO. Auto-generating new number.");
                    memberNo = await GenerateUniqueMemberNumberAsync(currentCompanyCode, registration);
                }

                // Validate CIG group if provided
                if (!string.IsNullOrEmpty(registration.Cigcode))
                {
                    var cigExists = await _context.CIGs
                        .AnyAsync(c => c.GigCode == registration.Cigcode && c.CompanyCode == currentCompanyCode);

                    if (!cigExists)
                    {
                        throw new ValidationException($"Selected CIG group {registration.Cigcode} is not valid for this company.");
                    }
                }

                // Get current user info
                var currentUserId = _companyContextService.GetCurrentUserId();
                var currentUserName = _companyContextService.GetCurrentUserName();

                // Create Member record with all fields from DTO
                var member = new Member
                {
                    // Core Fields
                    MemberNo = memberNo, // USE THE DETERMINED member number
                    Surname = registration.Surname,
                    OtherNames = registration.OtherNames,
                    FullName = $"{registration.Surname} {registration.OtherNames}".Trim(),

                    // Identification
                    Idno = registration.IdNo,

                    // Contact Information
                    PhoneNo = registration.PhoneNo,
                    HomeTelNo = registration.LandLine,
                    Email = registration.Email,
                    EmailAddress = registration.Email,

                    // Personal Details
                    Sex = registration.Gender,
                    Dob = registration.DateOfBirth,
                    Age = registration.Age ?? (registration.DateOfBirth.HasValue ?
                          CalculateAge(registration.DateOfBirth.Value) : (int?)null),

                    // Employment & Location
                    Station = registration.Station,
                    Dept = registration.Department,
                    PresentAddr = registration.PresentAddress,

                    // Company Information
                    CompanyCode = currentCompanyCode,
                    Cigcode = registration.Cigcode ?? currentCompanyCode,

                    // Membership Details
                    MembershipType = registration.MembershipType,
                    MemberDescription = registration.RegistrationType,

                    // Financial Information
                    ShareCap = registration.InitialShares,
                    InitShares = registration.InitialShares,
                    LoanBalance = 0,
                    InterestBalance = 0,

                    // Status Flags
                    Status = 1,
                    Mstatus = true,
                    Archived = false,
                    Withdrawn = false,
                    Dormant = 0,

                    // Dates
                    ApplicDate = registration.RegistrationDate,
                    EffectDate = DateTime.Now,
                    AsAtDate = DateTime.Now,
                    EDate = DateTime.Now,

                    // Audit Fields
                    Posted = "Y",
                    AuditId = currentUserName,
                    AuditTime = DateTime.Now,
                    AuditDateTime = DateTime.Now,

                    // Blockchain
                    BlockchainTxId = null
                };

                _logger.LogInformation($"Adding member to database with MemberNo: {memberNo} (from view: {registration.MemberNo}) for company: {currentCompanyCode}");
                _context.Members.Add(member);
                await _context.SaveChangesAsync();
                _logger.LogInformation($"Member saved to database successfully with MemberNo: {member.MemberNo}");

                // Rest of your blockchain and response code remains the same...
                try
                {
                    var blockchainData = new
                    {
                        MemberNo = memberNo,
                        FullName = $"{registration.Surname} {registration.OtherNames}",
                        IDNo = registration.IdNo,
                        Phone = registration.PhoneNo,
                        LandLine = registration.LandLine,
                        Email = registration.Email,
                        DateOfBirth = registration.DateOfBirth?.ToString("yyyy-MM-dd"),
                        Age = registration.Age,
                        Gender = registration.Gender,
                        Station = registration.Station,
                        Department = registration.Department,
                        PresentAddress = registration.PresentAddress,
                        CompanyCode = currentCompanyCode,
                        GroupCig = registration.Cigcode,
                        MembershipType = registration.MembershipType,
                        RegistrationType = registration.RegistrationType,
                        InitialShares = registration.InitialShares,
                        RegistrationDate = registration.RegistrationDate.ToString("yyyy-MM-dd HH:mm:ss"),
                        CreatedBy = currentUserName,
                        CreatedById = currentUserId,
                        Status = "ACTIVE"
                    };

                    _logger.LogInformation($"Creating blockchain transaction for member: {memberNo}");

                    var blockchainTx = await _blockchainService.CreateAndAddTransactionAsync(
                        "MEMBER_REGISTRATION",
                        memberNo,
                        currentCompanyCode,
                        registration.InitialShares,
                        memberNo,
                        blockchainData
                    );

                    if (blockchainTx == null)
                    {
                        _logger.LogWarning("Blockchain transaction creation returned null");
                    }
                    else
                    {
                        member.BlockchainTxId = blockchainTx.TransactionId;
                        await _context.SaveChangesAsync();
                        _logger.LogInformation($"Blockchain transaction ID saved: {blockchainTx.TransactionId}");
                    }

                    await transaction.CommitAsync();
                    _logger.LogInformation($"Transaction committed successfully for member: {memberNo}");

                    return new MemberResponseDTO
                    {
                        MemberNo = memberNo, // Return the SAME member number
                        FullName = $"{registration.Surname} {registration.OtherNames}",
                        Status = "ACTIVE",
                        RegistrationDate = registration.RegistrationDate,
                        BlockchainTxId = member.BlockchainTxId,
                        ShareBalance = registration.InitialShares,
                        Email = registration.Email,
                        Phone = registration.PhoneNo,
                        CompanyCode = currentCompanyCode,
                        MembershipType = registration.MembershipType,
                        RegistrationType = registration.RegistrationType
                    };
                }
                catch (Exception blockchainEx)
                {
                    _logger.LogError(blockchainEx, "Error with blockchain transaction, but member was saved to database");
                    await transaction.CommitAsync();

                    return new MemberResponseDTO
                    {
                        MemberNo = memberNo,
                        FullName = $"{registration.Surname} {registration.OtherNames}",
                        Status = "ACTIVE",
                        RegistrationDate = registration.RegistrationDate,
                        BlockchainTxId = null,
                        ShareBalance = registration.InitialShares,
                        Email = registration.Email,
                        Phone = registration.PhoneNo,
                        CompanyCode = currentCompanyCode,
                        MembershipType = registration.MembershipType,
                        RegistrationType = registration.RegistrationType
                    };
                }
            }
            catch (Exception ex)
            {
                // Rollback and error handling remains the same...
                try
                {
                    await transaction.RollbackAsync();
                    _logger.LogError(ex, "Transaction rolled back due to error");
                }
                catch (Exception rollbackEx)
                {
                    _logger.LogError(rollbackEx, "Error rolling back transaction");
                }

                if (ex is ValidationException)
                {
                    throw new Exception($"Validation error: {ex.Message}");
                }
                else if (ex is InvalidOperationException)
                {
                    throw new Exception(ex.Message);
                }
                else if (ex.InnerException != null && ex.InnerException.Message.Contains("UNIQUE KEY constraint"))
                {
                    if (ex.InnerException.Message.Contains("IX_Members_Idno"))
                        throw new Exception("Member with this ID number already exists. Please use a different ID number.");
                    else if (ex.InnerException.Message.Contains("IX_Members_PhoneNo"))
                        throw new Exception("Phone number is already registered to another member.");
                    else if (ex.InnerException.Message.Contains("IX_Members_Email"))
                        throw new Exception("Email address is already registered to another member.");
                    else
                        throw new Exception("A member with this information already exists.");
                }
                else if (ex.InnerException != null && ex.InnerException.Message.Contains("PRIMARY KEY"))
                {
                    throw new Exception("Duplicate member number detected. Please try again or contact administrator.");
                }

                throw new Exception($"Error registering member: {ex.Message}");
            }
        }

        //public async Task<MemberResponseDTO> RegisterMemberAsync(MemberRegistrationDTO registration)
        //{
        //    _logger.LogInformation($"Starting member registration for: {registration.Surname} {registration.OtherNames}");

        //    using var transaction = await _context.Database.BeginTransactionAsync();

        //    try
        //    {
        //        // Validate required fields
        //        if (string.IsNullOrEmpty(registration.Surname) || string.IsNullOrEmpty(registration.OtherNames))
        //        {
        //            throw new ValidationException("Surname and Other Names are required.");
        //        }

        //        if (string.IsNullOrEmpty(registration.IdNo))
        //        {
        //            throw new ValidationException("ID Number is required.");
        //        }

        //        // Get current user's company code
        //        var currentCompanyCode = _companyContextService.GetCurrentCompanyCode();
        //        _logger.LogInformation($"Using company code from current user context: {currentCompanyCode}");

        //        // Check for duplicate ID number within the same company
        //        var existingById = await _context.Members
        //            .FirstOrDefaultAsync(m => m.Idno == registration.IdNo && m.CompanyCode == currentCompanyCode);

        //        if (existingById != null)
        //        {
        //            throw new InvalidOperationException($"A member with ID number {registration.IdNo} already exists in company {currentCompanyCode}.");
        //        }

        //        // Check for duplicate phone number if provided
        //        if (!string.IsNullOrEmpty(registration.PhoneNo))
        //        {
        //            var existingByPhone = await _context.Members
        //                .FirstOrDefaultAsync(m => m.PhoneNo == registration.PhoneNo && m.CompanyCode == currentCompanyCode);

        //            if (existingByPhone != null)
        //            {
        //                throw new InvalidOperationException($"Phone number {registration.PhoneNo} is already registered to another member.");
        //            }
        //        }

        //        // Check for duplicate email if provided
        //        if (!string.IsNullOrEmpty(registration.Email))
        //        {
        //            var existingByEmail = await _context.Members
        //                .FirstOrDefaultAsync(m => m.Email == registration.Email && m.CompanyCode == currentCompanyCode);

        //            if (existingByEmail != null)
        //            {
        //                throw new InvalidOperationException($"Email {registration.Email} is already registered to another member.");
        //            }
        //        }

        //        // Generate unique member number (8-10 characters)
        //        string memberNo;

        //        // Check if user provided their own member number
        //        if (!string.IsNullOrEmpty(registration.MemberNo) && registration.MemberNo.Length >= 8 && registration.MemberNo.Length <= 10)
        //        {
        //            // Validate if the provided member number is available
        //            var existingByMemberNo = await _context.Members
        //                .FirstOrDefaultAsync(m => m.MemberNo == registration.MemberNo && m.CompanyCode == currentCompanyCode);

        //            if (existingByMemberNo == null)
        //            {
        //                memberNo = registration.MemberNo;
        //                _logger.LogInformation($"Using user-provided member number: {memberNo}");
        //            }
        //            else
        //            {
        //                _logger.LogWarning($"User-provided member number {registration.MemberNo} already exists. Generating new one.");
        //                memberNo = await GenerateUniqueMemberNumberAsync(currentCompanyCode, registration);
        //            }
        //        }
        //        else
        //        {
        //            // Auto-generate member number
        //            memberNo = await GenerateUniqueMemberNumberAsync(currentCompanyCode, registration);
        //        }

        //        // Validate CIG group if provided - FIXED to use GigCode property
        //        if (!string.IsNullOrEmpty(registration.Cigcode))
        //        {
        //            var cigExists = await _context.CIGs
        //                .AnyAsync(c => c.GigCode == registration.Cigcode && c.CompanyCode == currentCompanyCode);

        //            if (!cigExists)
        //            {
        //                throw new ValidationException($"Selected CIG group {registration.Cigcode} is not valid for this company.");
        //            }
        //        }

        //        // Get current user info
        //        var currentUserId = _companyContextService.GetCurrentUserId();
        //        var currentUserName = _companyContextService.GetCurrentUserName();

        //        // Create Member record with all fields from DTO
        //        var member = new Member
        //        {
        //            // Core Fields
        //            MemberNo = memberNo,
        //            Surname = registration.Surname,
        //            OtherNames = registration.OtherNames,
        //            FullName = $"{registration.Surname} {registration.OtherNames}".Trim(),

        //            // Identification
        //            Idno = registration.IdNo,

        //            // Contact Information
        //            PhoneNo = registration.PhoneNo,
        //            HomeTelNo = registration.LandLine, // LandLine mapped to HomeTelNo
        //            Email = registration.Email,
        //            EmailAddress = registration.Email,

        //            // Personal Details
        //            Sex = registration.Gender,
        //            Dob = registration.DateOfBirth,
        //            Age = registration.Age ?? (registration.DateOfBirth.HasValue ?
        //                  CalculateAge(registration.DateOfBirth.Value) : (int?)null),

        //            // Employment & Location
        //            Station = registration.Station,
        //            Dept = registration.Department,
        //            PresentAddr = registration.PresentAddress,

        //            // Company Information
        //            CompanyCode = currentCompanyCode,
        //            Cigcode = registration.Cigcode ?? currentCompanyCode, // Use selected CIG or default to company

        //            // Membership Details
        //            MembershipType = registration.MembershipType, // Individual / Corporate
        //            MemberDescription = registration.RegistrationType, // Board Member / Ordinary Member

        //            // Financial Information
        //            ShareCap = registration.InitialShares,
        //            InitShares = registration.InitialShares,
        //            LoanBalance = 0,
        //            InterestBalance = 0,

        //            // Status Flags
        //            Status = 1, // Active
        //            Mstatus = true,
        //            Archived = false,
        //            Withdrawn = false,
        //            Dormant = 0,

        //            // Dates
        //            ApplicDate = registration.RegistrationDate,
        //            EffectDate = DateTime.Now,
        //            AsAtDate = DateTime.Now,
        //            EDate = DateTime.Now,

        //            // Audit Fields
        //            Posted = "Y",
        //            AuditId = currentUserName,
        //            AuditTime = DateTime.Now,
        //            AuditDateTime = DateTime.Now,

        //            // Blockchain
        //            BlockchainTxId = null // Will be set later
        //        };

        //        _logger.LogInformation($"Adding member to database: {memberNo} for company: {currentCompanyCode}");
        //        _context.Members.Add(member);
        //        await _context.SaveChangesAsync();
        //        _logger.LogInformation($"Member saved to database successfully");

        //        try
        //        {
        //            // Create blockchain data with all member information
        //            var blockchainData = new
        //            {
        //                MemberNo = memberNo,
        //                FullName = $"{registration.Surname} {registration.OtherNames}",
        //                IDNo = registration.IdNo,
        //                Phone = registration.PhoneNo,
        //                LandLine = registration.LandLine,
        //                Email = registration.Email,
        //                DateOfBirth = registration.DateOfBirth?.ToString("yyyy-MM-dd"),
        //                Age = registration.Age,
        //                Gender = registration.Gender,
        //                Station = registration.Station,
        //                Department = registration.Department,
        //                PresentAddress = registration.PresentAddress,
        //                CompanyCode = currentCompanyCode,
        //                GroupCig = registration.Cigcode,
        //                MembershipType = registration.MembershipType,
        //                RegistrationType = registration.RegistrationType,
        //                InitialShares = registration.InitialShares,
        //                RegistrationDate = registration.RegistrationDate.ToString("yyyy-MM-dd HH:mm:ss"),
        //                CreatedBy = currentUserName,
        //                CreatedById = currentUserId,
        //                Status = "ACTIVE"
        //            };

        //            _logger.LogInformation($"Creating blockchain transaction for member: {memberNo}");

        //            // Create and add transaction to blockchain
        //            var blockchainTx = await _blockchainService.CreateAndAddTransactionAsync(
        //                "MEMBER_REGISTRATION",
        //                memberNo,
        //                currentCompanyCode,
        //                registration.InitialShares,
        //                memberNo,
        //                blockchainData
        //            );

        //            // Check if blockchain transaction was created successfully
        //            if (blockchainTx == null)
        //            {
        //                _logger.LogWarning("Blockchain transaction creation returned null");
        //                // Continue without blockchain - member is already saved
        //            }
        //            else
        //            {
        //                // Update member with blockchain transaction ID
        //                member.BlockchainTxId = blockchainTx.TransactionId;
        //                await _context.SaveChangesAsync();
        //                _logger.LogInformation($"Blockchain transaction ID saved: {blockchainTx.TransactionId}");
        //            }

        //            // Commit the transaction
        //            await transaction.CommitAsync();
        //            _logger.LogInformation($"Transaction committed successfully for member: {memberNo}");

        //            return new MemberResponseDTO
        //            {
        //                MemberNo = memberNo,
        //                FullName = $"{registration.Surname} {registration.OtherNames}",
        //                Status = "ACTIVE",
        //                RegistrationDate = registration.RegistrationDate,
        //                BlockchainTxId = member.BlockchainTxId,
        //                ShareBalance = registration.InitialShares,
        //                Email = registration.Email,
        //                Phone = registration.PhoneNo,
        //                CompanyCode = currentCompanyCode,
        //                MembershipType = registration.MembershipType,
        //                RegistrationType = registration.RegistrationType
        //            };
        //        }
        //        catch (Exception blockchainEx)
        //        {
        //            _logger.LogError(blockchainEx, "Error with blockchain transaction, but member was saved to database");

        //            await transaction.CommitAsync();

        //            return new MemberResponseDTO
        //            {
        //                MemberNo = memberNo,
        //                FullName = $"{registration.Surname} {registration.OtherNames}",
        //                Status = "ACTIVE",
        //                RegistrationDate = registration.RegistrationDate,
        //                BlockchainTxId = null,
        //                ShareBalance = registration.InitialShares,
        //                Email = registration.Email,
        //                Phone = registration.PhoneNo,
        //                CompanyCode = currentCompanyCode,
        //                MembershipType = registration.MembershipType,
        //                RegistrationType = registration.RegistrationType
        //            };
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        try
        //        {
        //            await transaction.RollbackAsync();
        //            _logger.LogError(ex, "Transaction rolled back due to error");
        //        }
        //        catch (Exception rollbackEx)
        //        {
        //            _logger.LogError(rollbackEx, "Error rolling back transaction");
        //        }

        //        if (ex is ValidationException)
        //        {
        //            throw new Exception($"Validation error: {ex.Message}");
        //        }
        //        else if (ex is InvalidOperationException)
        //        {
        //            throw new Exception(ex.Message);
        //        }
        //        else if (ex.InnerException != null && ex.InnerException.Message.Contains("UNIQUE KEY constraint"))
        //        {
        //            if (ex.InnerException.Message.Contains("IX_Members_Idno"))
        //                throw new Exception("Member with this ID number already exists. Please use a different ID number.");
        //            else if (ex.InnerException.Message.Contains("IX_Members_PhoneNo"))
        //                throw new Exception("Phone number is already registered to another member.");
        //            else if (ex.InnerException.Message.Contains("IX_Members_Email"))
        //                throw new Exception("Email address is already registered to another member.");
        //            else
        //                throw new Exception("A member with this information already exists.");
        //        }
        //        else if (ex.InnerException != null && ex.InnerException.Message.Contains("PRIMARY KEY"))
        //        {
        //            throw new Exception("Duplicate member number detected. Please try again or contact administrator.");
        //        }

        //        throw new Exception($"Error registering member: {ex.Message}");
        //    }
        //}

        public async Task<MemberResponseDTO> UpdateMemberAsync(string memberNo, MemberUpdateDTO updateDto)
        {
            _logger.LogInformation($"Starting member update for: {memberNo}");

            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var currentCompanyCode = _companyContextService.GetCurrentCompanyCode();
                var currentUserName = _companyContextService.GetCurrentUserName() ?? "SYSTEM";

                // Find the existing member
                var member = await _context.Members
                    .FirstOrDefaultAsync(m => m.MemberNo == memberNo && m.CompanyCode == currentCompanyCode);

                if (member == null)
                {
                    throw new InvalidOperationException($"Member {memberNo} not found.");
                }

                // Store old values for blockchain record
                var oldValues = new
                {
                    member.Idno,
                    member.ApplicDate,
                    member.Surname,
                    member.OtherNames,
                    member.PhoneNo,
                    member.HomeTelNo,
                    member.Email,
                    member.Sex,
                    member.Dob,
                    member.Station,
                    member.Dept,
                    member.PresentAddr,
                    member.Cigcode,
                    member.MembershipType,
                    member.MemberDescription,
                    member.Status,
                    member.Mstatus
                };

                // Update ID Number if provided and different
                if (!string.IsNullOrEmpty(updateDto.IdNo) && updateDto.IdNo != member.Idno)
                {
                    // Check if the new ID number already exists for another member
                    var existingWithId = await _context.Members
                        .FirstOrDefaultAsync(m => m.Idno == updateDto.IdNo &&
                                                 m.CompanyCode == currentCompanyCode &&
                                                 m.MemberNo != memberNo);

                    if (existingWithId != null)
                    {
                        throw new InvalidOperationException($"ID Number {updateDto.IdNo} is already registered to another member.");
                    }

                    member.Idno = updateDto.IdNo;
                }

                // Update Registration Date if provided - FIXED: RegistrationDate is DateTime, not nullable
                // Check if it's a valid date (not default/min value)
                if (updateDto.RegistrationDate != default(DateTime) && updateDto.RegistrationDate != DateTime.MinValue)
                {
                    member.ApplicDate = updateDto.RegistrationDate;
                }

                // Update other fields
                if (!string.IsNullOrEmpty(updateDto.Surname))
                    member.Surname = updateDto.Surname;

                if (!string.IsNullOrEmpty(updateDto.OtherNames))
                    member.OtherNames = updateDto.OtherNames;

                member.FullName = $"{member.Surname} {member.OtherNames}".Trim();

                if (!string.IsNullOrEmpty(updateDto.PhoneNo))
                    member.PhoneNo = updateDto.PhoneNo;

                if (updateDto.LandLine != null)
                    member.HomeTelNo = updateDto.LandLine;

                if (updateDto.Email != null)
                {
                    member.Email = updateDto.Email;
                    member.EmailAddress = updateDto.Email;
                }

                if (updateDto.Gender != null)
                    member.Sex = updateDto.Gender;

                if (updateDto.DateOfBirth.HasValue)
                {
                    member.Dob = updateDto.DateOfBirth;
                    member.Age = CalculateAge(updateDto.DateOfBirth.Value);
                }

                if (updateDto.Station != null)
                    member.Station = updateDto.Station;

                if (updateDto.Department != null)
                    member.Dept = updateDto.Department;

                if (updateDto.PresentAddress != null)
                    member.PresentAddr = updateDto.PresentAddress;

                if (updateDto.Cigcode != null)
                    member.Cigcode = updateDto.Cigcode;

                if (updateDto.MembershipType != null)
                    member.MembershipType = updateDto.MembershipType;

                if (updateDto.RegistrationType != null)
                    member.MemberDescription = updateDto.RegistrationType;

                // Update marital status
                if (!string.IsNullOrEmpty(updateDto.MaritalStatus))
                {
                    member.Mstatus = updateDto.MaritalStatus.ToUpper() == "MARRIED" ? true :
                                     updateDto.MaritalStatus.ToUpper() == "SINGLE" ? false : member.Mstatus;
                }

                // Update status
                if (!string.IsNullOrEmpty(updateDto.Status))
                {
                    member.Status = updateDto.Status switch
                    {
                        "Active" => 1,
                        "Withdrawn" => 2,
                        "Deceased" => 3,
                        "Dormant" => 4,
                        "Suspended" => 5,
                        _ => member.Status
                    };
                }

                // Update audit fields
                member.AuditId = currentUserName;
                member.AuditTime = DateTime.Now;
                member.AuditDateTime = DateTime.Now;

                await _context.SaveChangesAsync();
                _logger.LogInformation($"Member {memberNo} updated in database");

                // Create blockchain transaction for the update
                string blockchainTxId = null; // FIXED: Declare blockchainTxId here
                try
                {
                    var blockchainData = new
                    {
                        MemberNo = memberNo,
                        Action = "UPDATE",
                        OldValues = oldValues,
                        NewValues = new
                        {
                            member.Idno,
                            member.ApplicDate,
                            member.Surname,
                            member.OtherNames,
                            member.PhoneNo,
                            member.HomeTelNo,
                            member.Email,
                            member.Sex,
                            member.Dob,
                            member.Station,
                            member.Dept,
                            member.PresentAddr,
                            member.Cigcode,
                            member.MembershipType,
                            member.MemberDescription,
                            member.Status,
                            member.Mstatus
                        },
                        UpdatedBy = currentUserName,
                        UpdatedAt = DateTime.Now,
                        UpdateType = "MEMBER_UPDATE"
                    };

                    var blockchainTx = await _blockchainService.CreateAndAddTransactionAsync(
                        "MEMBER_UPDATE",
                        memberNo,
                        currentCompanyCode,
                        0, // No amount for update
                        memberNo,
                        blockchainData
                    );

                    if (blockchainTx != null)
                    {
                        blockchainTxId = blockchainTx.TransactionId;
                        member.BlockchainTxId = blockchainTxId;
                        await _context.SaveChangesAsync();
                        _logger.LogInformation($"Blockchain update transaction recorded: {blockchainTxId}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error recording blockchain update transaction, but member was updated");
                    // Don't throw - member update succeeded, blockchain recording failed
                }

                await transaction.CommitAsync();

                // Return response DTO - Now blockchainTxId is in scope
                return new MemberResponseDTO
                {
                    MemberNo = member.MemberNo,
                    FullName = $"{member.Surname} {member.OtherNames}".Trim(),
                    Status = member.Status switch
                    {
                        1 => "Active",
                        2 => "Withdrawn",
                        3 => "Deceased",
                        4 => "Dormant",
                        5 => "Suspended",
                        _ => "Unknown"
                    },
                    RegistrationDate = member.ApplicDate ?? DateTime.Now,
                    BlockchainTxId = blockchainTxId ?? member.BlockchainTxId,
                    ShareBalance = member.ShareCap ?? 0,
                    Email = member.Email,
                    Phone = member.PhoneNo,
                    CompanyCode = member.CompanyCode,
                    MembershipType = member.MembershipType,
                    RegistrationType = member.MemberDescription,
                    IsActive = member.Status == 1
                };
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, $"Error updating member {memberNo}");
                throw new Exception($"Error updating member: {ex.Message}");
            }
        }

        private async Task<string> GenerateUniqueMemberNumberAsync(string companyCode, MemberRegistrationDTO registration)
        {
            string memberNo = string.Empty;
            bool isUnique = false;
            int attempts = 0;
            const int maxAttempts = 5;

            do
            {
                attempts++;

                // Get month initial (first letter of current month in uppercase)
                string monthInitial = DateTime.Now.ToString("MMM").Substring(0, 1).ToUpper();

                // Generate 11 unique digits: yyMMddHHmmss (12 digits) but we'll take last 11 or generate differently
                // Option 1: Use timestamp (yyMMddHHmmss) - 12 digits, take last 11
                string timestamp = DateTime.Now.ToString("yyMMddHHmmss");
                string uniqueDigits = timestamp.Length > 11 ? timestamp.Substring(timestamp.Length - 11) : timestamp.PadLeft(11, '0');

                // Alternative Option 2: Use combination of date + random for more uniqueness
                // string datePart = DateTime.Now.ToString("yyMMdd"); // 6 digits
                // string randomPart = new Random().Next(10000, 99999).ToString(); // 5 digits
                // string uniqueDigits = $"{datePart}{randomPart}"; // 11 digits

                // Combine: Month Initial + 11 digits = 12 characters total
                memberNo = $"{monthInitial}{uniqueDigits}";

                // Ensure exact length (should be 12 characters)
                if (memberNo.Length > 12)
                {
                    memberNo = memberNo.Substring(0, 12);
                }
                else if (memberNo.Length < 12)
                {
                    // Pad with random numbers if too short
                    Random rand = new Random();
                    memberNo = memberNo.PadRight(12, (char)('0' + rand.Next(0, 9)));
                }

                // Check uniqueness
                var existing = await _context.Members
                    .FirstOrDefaultAsync(m => m.MemberNo == memberNo && m.CompanyCode == companyCode);

                isUnique = existing == null;

                if (!isUnique && attempts < maxAttempts)
                {
                    // Small delay to ensure different timestamp
                    await Task.Delay(100);
                }

            } while (!isUnique && attempts < maxAttempts);

            // If still not unique after max attempts
            if (!isUnique)
            {
                // Use GUID for guaranteed uniqueness
                string monthInitial = DateTime.Now.ToString("MMM").Substring(0, 1).ToUpper();
                string guidDigits = Guid.NewGuid().ToString().Replace("-", "").Substring(0, 11);
                memberNo = $"{monthInitial}{guidDigits}";

                // Final check
                var finalCheck = await _context.Members
                    .FirstOrDefaultAsync(m => m.MemberNo == memberNo && m.CompanyCode == companyCode);

                if (finalCheck != null)
                {
                    // Last resort: add random suffix
                    Random random = new Random();
                    string suffix = random.Next(100, 999).ToString();
                    memberNo = $"{memberNo.Substring(0, Math.Min(9, memberNo.Length))}{suffix}";
                }
            }

            _logger.LogInformation($"Generated member number: {memberNo} after {attempts} attempt(s)");
            return memberNo;
        }

        private int CalculateAge(DateTime dateOfBirth)
        {
            var today = DateTime.Today;
            var age = today.Year - dateOfBirth.Year;
            if (dateOfBirth.Date > today.AddYears(-age)) age--;
            return age;
        }

        public async Task<Member> GetMemberByMemberNoAsync(string memberNo)
        {
            var currentCompanyCode = _companyContextService.GetCurrentCompanyCode();

            return await _context.Members
                .FirstOrDefaultAsync(m => m.MemberNo == memberNo && m.CompanyCode == currentCompanyCode);
        }

        public async Task<List<Member>> SearchMembersAsync(string searchTerm)
        {
            var currentCompanyCode = _companyContextService.GetCurrentCompanyCode();

            return await _context.Members
                .Where(m => m.CompanyCode == currentCompanyCode &&
                           (m.MemberNo.Contains(searchTerm) ||
                            m.Surname.Contains(searchTerm) ||
                            m.OtherNames.Contains(searchTerm) ||
                            m.Idno.Contains(searchTerm)))
                .Take(50)
                .ToListAsync();
        }

        public async Task<decimal> GetMemberShareBalanceAsync(string memberNo)
        {
            var currentCompanyCode = _companyContextService.GetCurrentCompanyCode();

            // Get total share capital from Shares table
            var shareCapital = await _context.Shares
                .Where(s => s.MemberNo == memberNo && s.CompanyCode == currentCompanyCode)
                .SumAsync(s => s.TotalShares ?? 0);

            // Also get share capital from ContribShare table as backup
            var contribShareCapital = await _context.ContribShares
                .Where(cs => cs.MemberNo == memberNo && cs.CompanyCode == currentCompanyCode)
                .SumAsync(cs => cs.ShareCapitalAmount ?? 0);

            // Return the larger amount (they should match, but this handles discrepancies)
            var totalShareBalance = Math.Max(shareCapital, contribShareCapital);

            _logger.LogDebug($"Member {memberNo} share balance: {totalShareBalance:C} (Shares: {shareCapital:C}, ContribShares: {contribShareCapital:C})");

            return totalShareBalance;
        }

        public async Task<bool> UpdateMemberAsync(string memberNo, Member updatedMember)
        {
            var member = await GetMemberByMemberNoAsync(memberNo);
            if (member == null) return false;

            // Update editable fields (all except MemberNo, Idno, CompanyCode)
            member.Surname = updatedMember.Surname ?? member.Surname;
            member.OtherNames = updatedMember.OtherNames ?? member.OtherNames;
            member.FullName = updatedMember.FullName ?? member.FullName;

            // Contact Information
            member.PhoneNo = updatedMember.PhoneNo ?? member.PhoneNo;
            member.HomeTelNo = updatedMember.HomeTelNo ?? member.HomeTelNo;
            member.Email = updatedMember.Email ?? member.Email;
            member.EmailAddress = updatedMember.EmailAddress ?? member.EmailAddress;

            // Personal Details
            member.Sex = updatedMember.Sex ?? member.Sex;
            member.Dob = updatedMember.Dob ?? member.Dob;
            member.Age = updatedMember.Age ?? member.Age;
            member.Mstatus = updatedMember.Mstatus ?? member.Mstatus;

            // Employment & Location
            member.Employer = updatedMember.Employer ?? member.Employer;
            member.Dept = updatedMember.Dept ?? member.Dept;
            member.Station = updatedMember.Station ?? member.Station;
            member.PresentAddr = updatedMember.PresentAddr ?? member.PresentAddr;

            // Company Information
            member.Cigcode = updatedMember.Cigcode ?? member.Cigcode;

            // Membership Details
            member.MembershipType = updatedMember.MembershipType ?? member.MembershipType;
            member.MemberDescription = updatedMember.MemberDescription ?? member.MemberDescription;

            // Status
            if (updatedMember.Status.HasValue)
            {
                member.Status = updatedMember.Status.Value;
            }

            // Update audit fields
            member.AuditId = updatedMember.AuditId ?? member.AuditId;
            member.AuditTime = DateTime.Now;
            member.AuditDateTime = DateTime.Now;

            // Create blockchain transaction for update
            var updateData = new
            {
                MemberNo = memberNo,
                UpdateTime = DateTime.Now,
                UpdatedBy = member.AuditId,
                UpdatedFields = new
                {
                    Surname = updatedMember.Surname,
                    OtherNames = updatedMember.OtherNames,
                    PhoneNo = updatedMember.PhoneNo,
                    LandLine = updatedMember.HomeTelNo,
                    Email = updatedMember.Email,
                    Gender = updatedMember.Sex,
                    DateOfBirth = updatedMember.Dob,
                    MaritalStatus = updatedMember.Mstatus,
                    Employer = updatedMember.Employer,
                    Department = updatedMember.Dept,
                    Station = updatedMember.Station,
                    PresentAddress = updatedMember.PresentAddr,
                    Cigcode = updatedMember.Cigcode,
                    MembershipType = updatedMember.MembershipType,
                    RegistrationType = updatedMember.MemberDescription,
                    Status = updatedMember.Status
                }
            };

            var blockchainTx = await _blockchainService.CreateTransaction(
                "MEMBER_UPDATE",
                memberNo,
                member.CompanyCode,
                0,
                memberNo,
                updateData
            );

            member.BlockchainTxId = blockchainTx.TransactionId;

            await _context.SaveChangesAsync();
            await _blockchainService.AddToBlockchain(blockchainTx);

            return true;
        }

        public async Task<List<BlockchainTransaction>> GetMemberBlockchainHistoryAsync(string memberNo)
        {
            var currentCompanyCode = _companyContextService.GetCurrentCompanyCode();

            return await _blockchainService.GetMemberTransactions(memberNo, currentCompanyCode);
        }

        public async Task<List<Member>> GetAllMembersAsync()
        {
            var currentCompanyCode = _companyContextService.GetCurrentCompanyCode();

            return await _context.Members
                .Where(m => m.CompanyCode == currentCompanyCode && m.Status == 1)
                .OrderBy(m => m.Surname)
                .ToListAsync();
        }

        public async Task<decimal> GetShareBalanceAsync(string memberNo)
        {
            var currentCompanyCode = _companyContextService.GetCurrentCompanyCode();

            var share = await _context.Shares
                .FirstOrDefaultAsync(s => s.MemberNo == memberNo && s.CompanyCode == currentCompanyCode);

            return share?.TotalShares ?? 0;
        }


        public async Task<ContributionResponseDTO> AddContributionAsync(ContributionDTO contributionDto)
        {
            _logger.LogInformation($"Starting contribution addition for member: {contributionDto.MemberNo}");

            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // Validate member exists
                var member = await _context.Members
                    .FirstOrDefaultAsync(m => m.MemberNo == contributionDto.MemberNo &&
                                             m.CompanyCode == contributionDto.CompanyCode);

                if (member == null)
                {
                    throw new ValidationException($"Member {contributionDto.MemberNo} not found");
                }

                // Validate share type exists
                var shareType = await _context.Sharetypes
                    .FirstOrDefaultAsync(st => st.SharesCode == contributionDto.SharesCode &&
                                              st.CompanyCode == contributionDto.CompanyCode);

                if (shareType == null)
                {
                    throw new ValidationException($"Share type {contributionDto.SharesCode} not found");
                }

                // Validate amount against share type minimum
                if (contributionDto.Amount < shareType.MinAmount)
                {
                    throw new ValidationException($"Amount cannot be less than minimum of {shareType.MinAmount:C}");
                }

                // ============================================================
                // NEW VALIDATION: Check maximum contribution limit
                // ============================================================
                string contributionCategory = DetermineContributionType(shareType, contributionDto);

                // Get existing total for this member + sharetype combination
                decimal existingTotal = await GetExistingContributionTotalAsync(
                    contributionDto.MemberNo,
                    contributionDto.SharesCode,
                    contributionCategory,
                    contributionDto.CompanyCode);

                decimal newTotal = existingTotal + contributionDto.Amount;

                _logger.LogInformation($"Contribution check - Existing: {existingTotal:C}, New: {contributionDto.Amount:C}, Total: {newTotal:C}, Max: {(shareType.MaxAmount.HasValue ? shareType.MaxAmount.Value.ToString("C") : "Unlimited")}");

                // Check if this contribution would exceed the maximum
                if (shareType.MaxAmount.HasValue && newTotal > shareType.MaxAmount.Value)
                {
                    decimal remainingAllowed = shareType.MaxAmount.Value - existingTotal;
                    if (remainingAllowed <= 0)
                    {
                        throw new ValidationException(
                            $"Maximum {shareType.SharesType} limit of {shareType.MaxAmount.Value:C} has already been reached. " +
                            $"Current total: {existingTotal:C}. No further contributions allowed.");
                    }
                    else
                    {
                        throw new ValidationException(
                            $"Amount {contributionDto.Amount:C} exceeds remaining limit for {shareType.SharesType}. " +
                            $"Current total: {existingTotal:C}, Maximum: {shareType.MaxAmount.Value:C}, " +
                            $"Remaining allowed: {remainingAllowed:C}. Please reduce the amount.");
                    }
                }

                // Get Sacco parameters for default accounts
                var sacco = await _context.SaccoParram
                    .FirstOrDefaultAsync(s => s.CompanyCode == contributionDto.CompanyCode);

                // Generate receipt number if not provided
                var receiptNo = contributionDto.ReceiptNo ?? GenerateReceiptNumber(contributionDto.CompanyCode);

                // Store original values for audit (before any changes)
                var originalContribShare = await _context.ContribShares
                    .FirstOrDefaultAsync(cs => cs.MemberNo == contributionDto.MemberNo &&
                                               cs.Sharescode == contributionDto.SharesCode &&
                                               cs.CompanyCode == contributionDto.CompanyCode);

                // Create Contrib record
                var contrib = new Contrib
                {
                    MemberNo = contributionDto.MemberNo,
                    ContrDate = contributionDto.TransactionDate,
                    Amount = contributionDto.Amount,
                    CompanyCode = contributionDto.CompanyCode,
                    ReceiptNo = receiptNo,
                    Remarks = contributionDto.Remarks,
                    AuditId = contributionDto.CreatedBy,
                    AuditTime = DateTime.Now,
                    AuditDateTime = DateTime.Now,
                    Sharescode = contributionDto.SharesCode,
                    TransactionNo = Guid.NewGuid().ToString().Substring(0, 20),
                    Posted = "Y",
                    Locked = "N",
                    StaffNo = null,
                    DepositedDate = contributionDto.TransactionDate,
                    ReceiptDate = contributionDto.TransactionDate,
                    RefNo = contributionDto.ReferenceNo,
                    ShareBal = 0,
                    TransBy = contributionDto.CreatedBy,
                    ChequeNo = contributionDto.PaymentMethod == "CHEQUE" ? contributionDto.ReferenceNo : null,
                    TransDate = contributionDto.TransactionDate,
                    SharesAcc = shareType.SharesAcc,
                    ContraAcc = shareType.ContraAcc,
                    CashBookdate = DateTime.Now,
                    Dregard = 0,
                    Offs = 0,
                    ApiKey = null,
                    UserName = contributionDto.CreatedBy,
                    Run = 0,
                    Run2 = 0,
                    MrCleared = "N",
                    Mrno = null,
                    Offset = false,
                    TransferDesc = null,
                    Schemecode = contributionDto.CompanyCode
                };

                _context.Contribs.Add(contrib);

                // Create ContribShare record
                var contribShare = new ContribShare
                {
                    MemberNo = contributionDto.MemberNo,
                    CompanyCode = contributionDto.CompanyCode,
                    ReceiptNo = receiptNo,
                    Sharescode = contributionDto.SharesCode,
                    Remarks = contributionDto.Remarks,
                    AuditId = contributionDto.CreatedBy,
                    AuditTime = DateTime.Now,
                    AuditDateTime = DateTime.Now,
                    TransactionNo = contrib.TransactionNo,
                    ContrDate = contributionDto.TransactionDate,
                    LocalId = null,
                    LoanNo = null,
                    DepositedDate = contributionDto.TransactionDate,
                    ReceiptDate = contributionDto.TransactionDate,
                    ShareCapitalAmount = originalContribShare?.ShareCapitalAmount ?? 0,
                    DepositsAmount = originalContribShare?.DepositsAmount ?? 0,
                    PassBookAmount = originalContribShare?.PassBookAmount ?? 0,
                    Donor = originalContribShare?.Donor ?? 0,
                    LoanAmount = originalContribShare?.LoanAmount ?? 0,
                    RegFeeAmount = originalContribShare?.RegFeeAmount ?? 0
                };

                // ============================================================
                // GET GL ACCOUNTS - NO HARDCODING!
                // ============================================================

                string drAcc = null; // Debit account (Where money comes FROM - Bank/Cash)
                string crAcc = null; // Credit account (Where money goes TO - Share Type account)

                // STEP 1: SET CREDIT ACCOUNT FROM SHARETYPE (Database-driven)
                if (string.IsNullOrEmpty(shareType.SharesAcc))
                {
                    throw new Exception($"Share type '{shareType.SharesCode}' does not have a GL Account configured. Please configure SharesAcc in Sharetype.");
                }

                crAcc = shareType.SharesAcc;
                _logger.LogInformation($"Credit Account from Sharetype: {crAcc} - {shareType.SharesType}");

                // STEP 2: SET DEBIT ACCOUNT BASED ON PAYMENT METHOD
                string paymentMethod = contributionDto.PaymentMethod?.ToUpper() ?? "CASH";

                switch (paymentMethod)
                {
                    case "BANK TRANSFER":
                    case "CHEQUE":
                        if (!string.IsNullOrEmpty(contributionDto.ReferenceNo))
                        {
                            var bank = await _context.Banks
                                .FirstOrDefaultAsync(b => b.CompanyCode == contributionDto.CompanyCode &&
                                                          b.IsActive == true &&
                                                          (b.BankCode == contributionDto.ReferenceNo ||
                                                           b.BankName.Contains(contributionDto.ReferenceNo) ||
                                                           b.AccountNumber == contributionDto.ReferenceNo));

                            if (bank != null && !string.IsNullOrEmpty(bank.GlAccountNo))
                            {
                                drAcc = bank.GlAccountNo;
                                _logger.LogInformation($"Debit Account from Bank: {drAcc} - {bank.BankName}");
                                break;
                            }
                        }

                        var defaultBank = await _context.Banks
                            .FirstOrDefaultAsync(b => b.CompanyCode == contributionDto.CompanyCode &&
                                                      b.IsActive == true &&
                                                      !string.IsNullOrEmpty(b.GlAccountNo));

                        if (defaultBank != null)
                        {
                            drAcc = defaultBank.GlAccountNo;
                            _logger.LogInformation($"Debit Account from Default Bank: {drAcc} - {defaultBank.BankName}");
                            break;
                        }

                        goto case "CASH";

                    case "MOBILE MONEY":
                    case "CASH":
                    default:
                        if (sacco != null && !string.IsNullOrEmpty(sacco.RetainedEarnings))
                        {
                            drAcc = sacco.RetainedEarnings;
                            _logger.LogInformation($"Debit Account from SaccoParram CashAccount: {drAcc}");
                        }
                        else
                        {
                            var cashAccount = await _context.GlSetup
                                .FirstOrDefaultAsync(g => g.CompanyCode == contributionDto.CompanyCode &&
                                                          g.Status == true &&
                                                          (g.Type == "ASSET" || g.Type == "Asset") &&
                                                          (g.Glaccname != null &&
                                                           (g.Glaccname.ToLower().Contains("cash") ||
                                                            g.Glaccname.ToLower().Contains("bank"))));

                            if (cashAccount != null)
                            {
                                drAcc = cashAccount.AccNo;
                                _logger.LogInformation($"Debit Account from GlSetup Asset: {drAcc} - {cashAccount.Glaccname}");
                            }
                        }
                        break;
                }

                // STEP 3: VALIDATE ACCOUNTS EXIST IN GLSETUP
                if (string.IsNullOrEmpty(drAcc))
                {
                    var suspenseAccount = await _context.GlSetup
                        .FirstOrDefaultAsync(g => g.CompanyCode == contributionDto.CompanyCode &&
                                                  g.IsSuspense == true);

                    if (suspenseAccount != null)
                    {
                        drAcc = suspenseAccount.AccNo;
                        _logger.LogWarning($"Using Suspense Account as Debit: {drAcc}");
                    }
                    else
                    {
                        throw new Exception($"Cannot determine Debit Account for payment method '{paymentMethod}'. " +
                                           $"Please configure:\n" +
                                           $"1. For Bank/Cheque: Set up Banks with GL accounts\n" +
                                           $"2. For Cash: Set CashAccount in SaccoParram\n" +
                                           $"3. Or create an Asset account in GL Setup");
                    }
                }

                var drAccountValid = await _context.GlSetup
                    .AnyAsync(g => g.AccNo == drAcc &&
                                  g.CompanyCode == contributionDto.CompanyCode &&
                                  g.Status == true);

                if (!drAccountValid)
                {
                    throw new Exception($"Debit account '{drAcc}' not found or inactive in GL Setup for company {contributionDto.CompanyCode}");
                }

                var crAccountValid = await _context.GlSetup
                    .AnyAsync(g => g.AccNo == crAcc &&
                                  g.CompanyCode == contributionDto.CompanyCode &&
                                  g.Status == true);

                if (!crAccountValid)
                {
                    throw new Exception($"Credit account '{crAcc}' not found or inactive in GL Setup for company {contributionDto.CompanyCode}. " +
                                       $"Please configure SharesAcc for Sharetype '{shareType.SharesCode}'.");
                }

                _logger.LogInformation($"GL Transaction - DR: {drAcc}, CR: {crAcc}, Amount: {contributionDto.Amount:C}, " +
                                      $"Payment Method: {paymentMethod}, Type: {contributionCategory}");

                // Create GL Transaction
                var glTransaction = new Gltransaction
                {
                    TransDate = contributionDto.TransactionDate,
                    Amount = contributionDto.Amount,
                    DrAccNo = drAcc,
                    CrAccNo = crAcc,
                    Temp = "N",
                    DocumentNo = receiptNo,
                    Source = "CONTRIBUTION",
                    CompanyCode = contributionDto.CompanyCode,
                    TransDescript = $"{contributionCategory} - {shareType.SharesType} - {contributionDto.MemberNo}",
                    AuditTime = DateTime.Now,
                    AuditDateTime = DateTime.Now,
                    AuditId = contributionDto.CreatedBy,
                    Cash = paymentMethod == "CASH" ? 1 : 0,
                    DocPosted = 1,
                    ChequeNo = paymentMethod == "CHEQUE" ? contributionDto.ReferenceNo : null,
                    Dregard = false,
                    Recon = false,
                    TransactionNo = contrib.TransactionNo,
                    Module = "SHARES",
                    ReconId = 0
                };

                _context.Gltransactions.Add(glTransaction);
                await _context.SaveChangesAsync();

                // Store in appropriate column based on type and update totals
                decimal oldColumnValue = 0;
                decimal newColumnValue = 0;

                switch (contributionCategory)
                {
                    case "REGISTRATION_FEE":
                        oldColumnValue = contribShare.RegFeeAmount ?? 0;
                        contribShare.RegFeeAmount = (contribShare.RegFeeAmount ?? 0) + contributionDto.Amount;
                        newColumnValue = contribShare.RegFeeAmount ?? 0;
                        _logger.LogInformation($"✓ Contribution stored as REGISTRATION FEE: {contributionDto.Amount:C} (Total: {newColumnValue:C})");
                        break;

                    case "DEPOSIT":
                        oldColumnValue = contribShare.DepositsAmount ?? 0;
                        contribShare.DepositsAmount = (contribShare.DepositsAmount ?? 0) + contributionDto.Amount;
                        newColumnValue = contribShare.DepositsAmount ?? 0;
                        _logger.LogInformation($"✓ Contribution stored as DEPOSIT/SAVINGS: {contributionDto.Amount:C} (Total: {newColumnValue:C})");
                        break;

                    case "DONOR":
                        oldColumnValue = contribShare.Donor ?? 0;
                        contribShare.Donor = (contribShare.Donor ?? 0) + contributionDto.Amount;
                        newColumnValue = contribShare.Donor ?? 0;
                        _logger.LogInformation($"✓ Contribution stored as DONOR/GIFT: {contributionDto.Amount:C} (Total: {newColumnValue:C})");
                        break;

                    case "LOAN_REPAYMENT":
                        oldColumnValue = contribShare.LoanAmount ?? 0;
                        contribShare.LoanAmount = (contribShare.LoanAmount ?? 0) + contributionDto.Amount;
                        newColumnValue = contribShare.LoanAmount ?? 0;
                        _logger.LogInformation($"✓ Contribution stored as LOAN REPAYMENT: {contributionDto.Amount:C} (Total: {newColumnValue:C})");
                        break;

                    case "PASSBOOK":
                        oldColumnValue = contribShare.PassBookAmount ?? 0;
                        contribShare.PassBookAmount = (contribShare.PassBookAmount ?? 0) + contributionDto.Amount;
                        newColumnValue = contribShare.PassBookAmount ?? 0;
                        _logger.LogInformation($"✓ Contribution stored as PASSBOOK: {contributionDto.Amount:C} (Total: {newColumnValue:C})");
                        if (shareType.Issharecapital == 1)
                        {
                            await UpdateShareBalanceAsync(contributionDto.MemberNo,
                                contributionDto.SharesCode,
                                contributionDto.Amount,
                                contributionDto.CompanyCode);
                        }
                        break;

                    case "SHARE_CAPITAL":
                    default:
                        oldColumnValue = contribShare.ShareCapitalAmount ?? 0;
                        contribShare.ShareCapitalAmount = (contribShare.ShareCapitalAmount ?? 0) + contributionDto.Amount;
                        newColumnValue = contribShare.ShareCapitalAmount ?? 0;
                        _logger.LogInformation($"✓ Contribution stored as SHARE CAPITAL: {contributionDto.Amount:C} (Total: {newColumnValue:C})");
                        await UpdateShareBalanceAsync(contributionDto.MemberNo,
                            contributionDto.SharesCode,
                            contributionDto.Amount,
                            contributionDto.CompanyCode);
                        break;
                }

                // Check if the ContribShare record already exists, if not add it
                var existingContribShare = await _context.ContribShares
                    .FirstOrDefaultAsync(cs => cs.MemberNo == contributionDto.MemberNo &&
                                               cs.Sharescode == contributionDto.SharesCode &&
                                               cs.CompanyCode == contributionDto.CompanyCode);

                if (existingContribShare == null)
                {
                    _context.ContribShares.Add(contribShare);
                }
                else
                {
                    // Update existing record
                    existingContribShare.ShareCapitalAmount = contribShare.ShareCapitalAmount;
                    existingContribShare.DepositsAmount = contribShare.DepositsAmount;
                    existingContribShare.PassBookAmount = contribShare.PassBookAmount;
                    existingContribShare.Donor = contribShare.Donor;
                    existingContribShare.LoanAmount = contribShare.LoanAmount;
                    existingContribShare.RegFeeAmount = contribShare.RegFeeAmount;
                    existingContribShare.AuditTime = DateTime.Now;
                    existingContribShare.AuditDateTime = DateTime.Now;
                    existingContribShare.AuditId = contributionDto.CreatedBy;
                    existingContribShare.Remarks = contributionDto.Remarks;
                }

                await _context.SaveChangesAsync();

                // ============================================================
                // CREATE BLOCK AND BLOCKCHAIN TRANSACTION
                // ============================================================

                string blockHash = Guid.NewGuid().ToString().Replace("-", "");
                if (blockHash.Length < 64) blockHash = blockHash.PadRight(64, '0');
                else if (blockHash.Length > 64) blockHash = blockHash.Substring(0, 64);

                var block = new Block
                {
                    BlockHash = blockHash,
                    PreviousHash = await GetLastBlockHashAsync(),
                    Timestamp = DateTime.Now,
                    Nonce = 0,
                    MerkleRoot = Guid.NewGuid().ToString(),
                    Confirmed = true,
                    CreatedAt = DateTime.Now
                };

                _context.Blocks.Add(block);
                await _context.SaveChangesAsync();

                var blockchainData = new
                {
                    TransactionType = "CONTRIBUTION",
                    MemberNo = contributionDto.MemberNo,
                    MemberName = $"{member.Surname} {member.OtherNames}",
                    ShareType = shareType.SharesType,
                    ShareTypeCode = shareType.SharesCode,
                    ContributionCategory = contributionCategory,
                    Amount = contributionDto.Amount,
                    ReceiptNo = receiptNo,
                    TransactionDate = contributionDto.TransactionDate.ToString("yyyy-MM-dd HH:mm:ss"),
                    PaymentMethod = paymentMethod,
                    ReferenceNo = contributionDto.ReferenceNo,
                    Remarks = contributionDto.Remarks,
                    CompanyCode = contributionDto.CompanyCode,
                    CreatedBy = contributionDto.CreatedBy,
                    DrAccount = drAcc,
                    CrAccount = crAcc,
                    BlockHash = blockHash,
                    PreviousTotal = oldColumnValue,
                    NewTotal = newColumnValue,
                    MaxLimit = shareType.MaxAmount,
                    RemainingLimit = shareType.MaxAmount.HasValue ? shareType.MaxAmount.Value - newColumnValue : (decimal?)null
                };

                _logger.LogInformation($"Creating blockchain transaction for contribution: {receiptNo}");

                var blockchainTx = new BlockchainTransaction
                {
                    TransactionId = Guid.NewGuid().ToString(),
                    TransactionType = "CONTRIBUTION",
                    MemberNo = contributionDto.MemberNo,
                    CompanyCode = contributionDto.CompanyCode,
                    Amount = contributionDto.Amount,
                    Timestamp = DateTime.Now,
                    DataHash = await _blockchainService.GenerateTransactionHash(blockchainData),
                    PayloadJson = System.Text.Json.JsonSerializer.Serialize(blockchainData),
                    OffChainReferenceId = receiptNo,
                    Status = "CONFIRMED",
                    BlockHash = block.BlockHash,
                    CreatedAt = DateTime.Now
                };

                _context.BlockchainTransactions.Add(blockchainTx);
                await _context.SaveChangesAsync();

                contrib.BlockchainTxId = blockchainTx.TransactionId;
                if (existingContribShare != null)
                {
                    existingContribShare.BlockchainTxId = blockchainTx.TransactionId;
                }
                else
                {
                    contribShare.BlockchainTxId = blockchainTx.TransactionId;
                }
                glTransaction.BlockchainTxId = blockchainTx.TransactionId;
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();

                _logger.LogInformation($"Contribution {receiptNo} added successfully for member {contributionDto.MemberNo}");

                // ============================================================
                // SAVE AUDIT TRAIL (Like Member Registration)
                // ============================================================

                var auditExtraData = new
                {
                    amount = contributionDto.Amount,
                    memberName = $"{member.Surname} {member.OtherNames}",
                    memberNumber = contributionDto.MemberNo,
                    shareType = shareType.SharesType,
                    shareTypeCode = contributionDto.SharesCode,
                    contributionCategory = contributionCategory,
                    receiptNumber = receiptNo,
                    transactionDate = contributionDto.TransactionDate.ToString("yyyy-MM-dd HH:mm:ss"),
                    paymentMethod = paymentMethod,
                    referenceNo = contributionDto.ReferenceNo ?? "",
                    remarks = contributionDto.Remarks ?? "",
                    previousTotal = oldColumnValue,
                    newTotal = newColumnValue,
                    maxLimit = shareType.MaxAmount,
                    remainingLimit = shareType.MaxAmount.HasValue ? shareType.MaxAmount.Value - newColumnValue : (decimal?)null,
                    drAccount = drAcc,
                    crAccount = crAcc,
                    blockchainTxId = blockchainTx.TransactionId
                };

                // Create a copy of the contrib object for NewValue (what was just saved)
                var contribForAudit = new
                {
                    contrib.Id,
                    contrib.MemberNo,
                    contrib.ContrDate,
                    contrib.Amount,
                    contrib.ReceiptNo,
                    contrib.Remarks,
                    contrib.Sharescode,
                    contrib.TransactionNo,
                    contrib.RefNo,
                    contrib.ChequeNo,
                    contrib.TransDate,
                    contrib.SharesAcc,
                    contrib.ContraAcc,
                    contrib.UserName,
                    contrib.CompanyCode,
                    BlockchainTxId = blockchainTx.TransactionId,
                    CreatedAt = DateTime.Now,
                    CreatedBy = contributionDto.CreatedBy
                };

                await _auditService.SaveLogAsync(
                    actionType: AuditActionType.Insert,
                    oldModel: null,  // For Insert, OldValue is null (nothing existed before)
                    newModel: contribForAudit,  // This will be serialized to NewValue column
                    tableName: "Contribs",
                    recordId: receiptNo,
                    userId: contributionDto.CreatedBy,
                    userName: contributionDto.CreatedBy,
                    companyCode: contributionDto.CompanyCode,
                    module: "Contributions",
                    extraData: System.Text.Json.JsonSerializer.Serialize(auditExtraData),
                    blockchainTxId: blockchainTx.TransactionId
                );

                // Get current share balance
                var shareBalance = await GetMemberShareBalanceAsync(contributionDto.MemberNo);

                return new ContributionResponseDTO
                {
                    Id = contrib.Id,
                    MemberNo = contributionDto.MemberNo,
                    MemberName = $"{member.Surname} {member.OtherNames}",
                    TransactionDate = contributionDto.TransactionDate,
                    SharesCode = contributionDto.SharesCode,
                    ShareTypeName = shareType.SharesType ?? shareType.SharesCode,
                    Amount = contributionDto.Amount,
                    ShareCapitalAmount = contribShare.ShareCapitalAmount ?? 0,
                    DepositsAmount = contribShare.DepositsAmount ?? 0,
                    RegFeeAmount = contribShare.RegFeeAmount ?? 0,
                    Donor = contribShare.Donor ?? 0,
                    LoanAmount = contribShare.LoanAmount ?? 0,
                    PassBookAmount = contribShare.PassBookAmount ?? 0,
                    TotalSharesAfter = shareBalance,
                    ReceiptNo = receiptNo,
                    Remarks = contributionDto.Remarks ?? string.Empty,
                    BlockchainTxId = contrib.BlockchainTxId ?? string.Empty,
                    CreatedAt = DateTime.Now,
                    CreatedBy = contributionDto.CreatedBy,
                    CompanyCode = contributionDto.CompanyCode
                };
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Transaction rolled back due to error");

                if (ex is ValidationException)
                {
                    throw new Exception($"Validation error: {ex.Message}");
                }

                throw new Exception($"Error adding contribution: {ex.Message}");
            }
        }

        // ============================================================
        // HELPER METHOD: Get existing contribution total for a member + sharetype
        // ============================================================
        private async Task<decimal> GetExistingContributionTotalAsync(string memberNo, string sharesCode, string contributionCategory, string companyCode)
        {
            var contribShare = await _context.ContribShares
                .FirstOrDefaultAsync(cs => cs.MemberNo == memberNo &&
                                           cs.Sharescode == sharesCode &&
                                           cs.CompanyCode == companyCode);

            if (contribShare == null)
            {
                return 0;
            }

            switch (contributionCategory)
            {
                case "REGISTRATION_FEE":
                    return contribShare.RegFeeAmount ?? 0;
                case "DEPOSIT":
                    return contribShare.DepositsAmount ?? 0;
                case "DONOR":
                    return contribShare.Donor ?? 0;
                case "LOAN_REPAYMENT":
                    return contribShare.LoanAmount ?? 0;
                case "PASSBOOK":
                    return contribShare.PassBookAmount ?? 0;
                case "SHARE_CAPITAL":
                default:
                    return contribShare.ShareCapitalAmount ?? 0;
            }
        }



        /// <summary>
        /// Gets the total contributions for a specific member and share type
        /// </summary>
        public async Task<decimal> GetMemberShareTypeTotalAsync(string memberNo, string shareTypeCode, string companyCode)
        {
            var contribShare = await _context.ContribShares
                .FirstOrDefaultAsync(cs => cs.MemberNo == memberNo &&
                                           cs.Sharescode == shareTypeCode &&
                                           cs.CompanyCode == companyCode);

            if (contribShare == null)
            {
                return 0;
            }

            // Return the SPECIFIC column based on share type configuration
            // NOT summing all columns - that was the bug!
            var shareType = await _context.Sharetypes
                .FirstOrDefaultAsync(st => st.SharesCode == shareTypeCode && st.CompanyCode == companyCode);

            if (shareType == null)
            {
                return 0;
            }

            // Determine which column to check based on share type characteristics
            if (shareType.Issharecapital == 1 || shareType.IsMainShares == true)
            {
                return contribShare.ShareCapitalAmount ?? 0;
            }
            else if (shareType.Withdrawable == true)
            {
                return contribShare.DepositsAmount ?? 0;
            }
            else if (shareType.SharesType?.ToLower().Contains("fee") == true)
            {
                return contribShare.RegFeeAmount ?? 0;
            }
            else if (shareType.SharesType?.ToLower().Contains("donor") == true)
            {
                return contribShare.Donor ?? 0;
            }
            else if (shareType.SharesType?.ToLower().Contains("loan") == true)
            {
                return contribShare.LoanAmount ?? 0;
            }
            else
            {
                // Default to share capital
                return contribShare.ShareCapitalAmount ?? 0;
            }
        }



        //// ============================================================
        //// HELPER METHOD: Determine contribution type
        //// ============================================================
        //private string DetermineContributionType(Sharetype shareType, ContributionDTO contributionDto)
        //{
        //    // You can customize this logic based on ShareType properties
        //    // For example, based on shareType.SharesType or shareType.Issharecapital

        //    if (shareType.SharesType?.ToUpper().Contains("REGISTRATION") == true ||
        //        shareType.SharesType?.ToUpper().Contains("FEE") == true)
        //    {
        //        return "REGISTRATION_FEE";
        //    }

        //    if (shareType.SharesType?.ToUpper().Contains("DEPOSIT") == true ||
        //        shareType.SharesType?.ToUpper().Contains("SAVINGS") == true)
        //    {
        //        return "DEPOSIT";
        //    }

        //    if (shareType.SharesType?.ToUpper().Contains("DONOR") == true ||
        //        shareType.SharesType?.ToUpper().Contains("GIFT") == true)
        //    {
        //        return "DONOR";
        //    }

        //    if (shareType.SharesType?.ToUpper().Contains("LOAN") == true ||
        //        shareType.SharesType?.ToUpper().Contains("REPAYMENT") == true)
        //    {
        //        return "LOAN_REPAYMENT";
        //    }

        //    if (shareType.SharesType?.ToUpper().Contains("PASSBOOK") == true)
        //    {
        //        return "PASSBOOK";
        //    }

        //    // Default to Share Capital
        //    return "SHARE_CAPITAL";
        //}

        //public async Task<ContributionResponseDTO> AddContributionAsync(ContributionDTO contributionDto)
        //{
        //    _logger.LogInformation($"Starting contribution addition for member: {contributionDto.MemberNo}");

        //    using var transaction = await _context.Database.BeginTransactionAsync();

        //    try
        //    {
        //        // Validate member exists
        //        var member = await _context.Members
        //            .FirstOrDefaultAsync(m => m.MemberNo == contributionDto.MemberNo &&
        //                                     m.CompanyCode == contributionDto.CompanyCode);

        //        if (member == null)
        //        {
        //            throw new ValidationException($"Member {contributionDto.MemberNo} not found");
        //        }

        //        // Validate share type exists
        //        var shareType = await _context.Sharetypes
        //            .FirstOrDefaultAsync(st => st.SharesCode == contributionDto.SharesCode &&
        //                                      st.CompanyCode == contributionDto.CompanyCode);

        //        if (shareType == null)
        //        {
        //            throw new ValidationException($"Share type {contributionDto.SharesCode} not found");
        //        }

        //        // Validate amount against share type limits
        //        if (contributionDto.Amount < shareType.MinAmount)
        //        {
        //            throw new ValidationException($"Amount cannot be less than minimum of {shareType.MinAmount:C}");
        //        }

        //        if (shareType.MaxAmount.HasValue && contributionDto.Amount > shareType.MaxAmount.Value)
        //        {
        //            throw new ValidationException($"Amount cannot exceed maximum of {shareType.MaxAmount:C}");
        //        }

        //        // Get Sacco parameters for default accounts
        //        var sacco = await _context.SaccoParram
        //            .FirstOrDefaultAsync(s => s.CompanyCode == contributionDto.CompanyCode);

        //        // Generate receipt number if not provided
        //        var receiptNo = contributionDto.ReceiptNo ?? GenerateReceiptNumber(contributionDto.CompanyCode);

        //        // Create Contrib record
        //        var contrib = new Contrib
        //        {
        //            MemberNo = contributionDto.MemberNo,
        //            ContrDate = contributionDto.TransactionDate,
        //            Amount = contributionDto.Amount,
        //            CompanyCode = contributionDto.CompanyCode,
        //            ReceiptNo = receiptNo,
        //            Remarks = contributionDto.Remarks,
        //            AuditId = contributionDto.CreatedBy,
        //            AuditTime = DateTime.Now,
        //            AuditDateTime = DateTime.Now,
        //            Sharescode = contributionDto.SharesCode,
        //            TransactionNo = Guid.NewGuid().ToString().Substring(0, 20),
        //            Posted = "Y",
        //            Locked = "N",
        //            StaffNo = null,
        //            DepositedDate = contributionDto.TransactionDate,
        //            ReceiptDate = contributionDto.TransactionDate,
        //            RefNo = contributionDto.ReferenceNo,
        //            ShareBal = 0,
        //            TransBy = contributionDto.CreatedBy,
        //            ChequeNo = contributionDto.PaymentMethod == "CHEQUE" ? contributionDto.ReferenceNo : null,
        //            TransDate = contributionDto.TransactionDate,
        //            SharesAcc = shareType.SharesAcc,
        //            ContraAcc = shareType.ContraAcc,
        //            CashBookdate = DateTime.Now,
        //            Dregard = 0,
        //            Offs = 0,
        //            ApiKey = null,
        //            UserName = contributionDto.CreatedBy,
        //            Run = 0,
        //            Run2 = 0,
        //            MrCleared = "N",
        //            Mrno = null,
        //            Offset = false,
        //            TransferDesc = null,
        //            Schemecode = contributionDto.CompanyCode
        //        };

        //        _context.Contribs.Add(contrib);

        //        // Create ContribShare record
        //        var contribShare = new ContribShare
        //        {
        //            MemberNo = contributionDto.MemberNo,
        //            CompanyCode = contributionDto.CompanyCode,
        //            ReceiptNo = receiptNo,
        //            Sharescode = contributionDto.SharesCode,
        //            Remarks = contributionDto.Remarks,
        //            AuditId = contributionDto.CreatedBy,
        //            AuditTime = DateTime.Now,
        //            AuditDateTime = DateTime.Now,
        //            TransactionNo = contrib.TransactionNo,
        //            ContrDate = contributionDto.TransactionDate,
        //            LocalId = null,
        //            LoanNo = null,
        //            DepositedDate = contributionDto.TransactionDate,
        //            ReceiptDate = contributionDto.TransactionDate,
        //            ShareCapitalAmount = 0,
        //            DepositsAmount = 0,
        //            PassBookAmount = 0,
        //            Donor = 0,
        //            LoanAmount = 0,
        //            RegFeeAmount = 0
        //        };

        //        // ============================================================
        //        // GET GL ACCOUNTS - NO HARDCODING!
        //        // ============================================================

        //        // Determine contribution type (for logging only, not for hardcoded accounts)
        //        string sharetypeCategory = DetermineContributionType(shareType, contributionDto);

        //        string drAcc = null; // Debit account (Where money comes FROM - Bank/Cash)
        //        string crAcc = null; // Credit account (Where money goes TO - Share Type account)

        //        // ============================================================
        //        // STEP 1: SET CREDIT ACCOUNT FROM SHARETYPE (Database-driven)
        //        // ============================================================

        //        // The credit account should ALWAYS come from the ShareType configuration
        //        if (string.IsNullOrEmpty(shareType.SharesAcc))
        //        {
        //            throw new Exception($"Share type '{shareType.SharesCode}' does not have a GL Account configured. Please configure SharesAcc in Sharetype.");
        //        }

        //        crAcc = shareType.SharesAcc;
        //        _logger.LogInformation($"Credit Account from Sharetype: {crAcc} - {shareType.SharesType}");

        //        // ============================================================
        //        // STEP 2: SET DEBIT ACCOUNT BASED ON PAYMENT METHOD
        //        // ============================================================

        //        // Determine the debit account based on payment method
        //        string paymentMethod = contributionDto.PaymentMethod?.ToUpper() ?? "CASH";

        //        switch (paymentMethod)
        //        {
        //            case "BANK TRANSFER":
        //            case "CHEQUE":
        //                // For bank transfers and cheques, use the bank's GL account
        //                if (!string.IsNullOrEmpty(contributionDto.ReferenceNo))
        //                {
        //                    // Try to find bank by reference number (could be bank code or bank name)
        //                    var bank = await _context.Banks
        //                        .FirstOrDefaultAsync(b => b.CompanyCode == contributionDto.CompanyCode &&
        //                                                  b.IsActive == true &&
        //                                                  (b.BankCode == contributionDto.ReferenceNo ||
        //                                                   b.BankName.Contains(contributionDto.ReferenceNo) ||
        //                                                   b.AccountNumber == contributionDto.ReferenceNo));

        //                    if (bank != null && !string.IsNullOrEmpty(bank.GlAccountNo))
        //                    {
        //                        drAcc = bank.GlAccountNo;
        //                        _logger.LogInformation($"Debit Account from Bank: {drAcc} - {bank.BankName}");
        //                        break;
        //                    }
        //                }

        //                // If no specific bank found, try to get default bank
        //                var defaultBank = await _context.Banks
        //                    .FirstOrDefaultAsync(b => b.CompanyCode == contributionDto.CompanyCode &&
        //                                              b.IsActive == true &&
        //                                              !string.IsNullOrEmpty(b.GlAccountNo));

        //                if (defaultBank != null)
        //                {
        //                    drAcc = defaultBank.GlAccountNo;
        //                    _logger.LogInformation($"Debit Account from Default Bank: {drAcc} - {defaultBank.BankName}");
        //                    break;
        //                }

        //                // Fall through to cash account if no bank found
        //                goto case "CASH";

        //                case "MOBILE MONEY":
        //                case "CASH":
        //                default:
        //                // For cash/mobile payments, use the Cash Account from SaccoParram
        //                if (sacco != null && !string.IsNullOrEmpty(sacco.RetainedEarnings))
        //                {
        //                    drAcc = sacco.RetainedEarnings;
        //                    _logger.LogInformation($"Debit Account from SaccoParram CashAccount: {drAcc}");
        //                }
        //                else
        //                {
        //                    // If no cash account in SaccoParram, find any active Asset account
        //                    var cashAccount = await _context.GlSetup
        //                        .FirstOrDefaultAsync(g => g.CompanyCode == contributionDto.CompanyCode &&
        //                                                  g.Status == true &&
        //                                                  (g.Type == "ASSET" || g.Type == "Asset") &&
        //                                                  (g.Glaccname != null &&
        //                                                   (g.Glaccname.ToLower().Contains("cash") ||
        //                                                    g.Glaccname.ToLower().Contains("bank"))));

        //                    if (cashAccount != null)
        //                    {
        //                        drAcc = cashAccount.AccNo;
        //                        _logger.LogInformation($"Debit Account from GlSetup Asset: {drAcc} - {cashAccount.Glaccname}");
        //                    }
        //                }
        //                break;
        //        }

        //        // ============================================================
        //        // STEP 3: VALIDATE ACCOUNTS EXIST IN GLSETUP
        //        // ============================================================

        //        if (string.IsNullOrEmpty(drAcc))
        //        {
        //            // Last resort - use Suspense account
        //            var suspenseAccount = await _context.GlSetup
        //                .FirstOrDefaultAsync(g => g.CompanyCode == contributionDto.CompanyCode &&
        //                                          g.IsSuspense == true);

        //            if (suspenseAccount != null)
        //            {
        //                drAcc = suspenseAccount.AccNo;
        //                _logger.LogWarning($"Using Suspense Account as Debit: {drAcc}");
        //            }
        //            else
        //            {
        //                throw new Exception($"Cannot determine Debit Account for payment method '{paymentMethod}'. " +
        //                                   $"Please configure:\n" +
        //                                   $"1. For Bank/Cheque: Set up Banks with GL accounts\n" +
        //                                   $"2. For Cash: Set CashAccount in SaccoParram\n" +
        //                                   $"3. Or create an Asset account in GL Setup");
        //            }
        //        }

        //        // Validate DR account exists and is active
        //        var drAccountValid = await _context.GlSetup
        //            .AnyAsync(g => g.AccNo == drAcc &&
        //                          g.CompanyCode == contributionDto.CompanyCode &&
        //                          g.Status == true);

        //        if (!drAccountValid)
        //        {
        //            throw new Exception($"Debit account '{drAcc}' not found or inactive in GL Setup for company {contributionDto.CompanyCode}");
        //        }

        //        // Validate CR account exists and is active
        //        var crAccountValid = await _context.GlSetup
        //            .AnyAsync(g => g.AccNo == crAcc &&
        //                          g.CompanyCode == contributionDto.CompanyCode &&
        //                          g.Status == true);

        //        if (!crAccountValid)
        //        {
        //            throw new Exception($"Credit account '{crAcc}' not found or inactive in GL Setup for company {contributionDto.CompanyCode}. " +
        //                               $"Please configure SharesAcc for Sharetype '{shareType.SharesCode}'.");
        //        }

        //        _logger.LogInformation($"GL Transaction - DR: {drAcc}, CR: {crAcc}, Amount: {contributionDto.Amount:C}, " +
        //                              $"Payment Method: {paymentMethod}, Type: {sharetypeCategory}");

        //        // Create GL Transaction
        //        var glTransaction = new Gltransaction
        //        {
        //            TransDate = contributionDto.TransactionDate,
        //            Amount = contributionDto.Amount,
        //            DrAccNo = drAcc,
        //            CrAccNo = crAcc,
        //            Temp = "N",
        //            DocumentNo = receiptNo,
        //            Source = "CONTRIBUTION",
        //            CompanyCode = contributionDto.CompanyCode,
        //            TransDescript = $"{sharetypeCategory} - {shareType.SharesType} - {contributionDto.MemberNo}",
        //            AuditTime = DateTime.Now,
        //            AuditDateTime = DateTime.Now,
        //            AuditId = contributionDto.CreatedBy,
        //            Cash = paymentMethod == "CASH" ? 1 : 0,
        //            DocPosted = 1,
        //            ChequeNo = paymentMethod == "CHEQUE" ? contributionDto.ReferenceNo : null,
        //            Dregard = false,
        //            Recon = false,
        //            TransactionNo = contrib.TransactionNo,
        //            Module = "SHARES",
        //            ReconId = 0
        //        };

        //        _context.Gltransactions.Add(glTransaction);

        //        // Save all changes before proceeding
        //        await _context.SaveChangesAsync();

        //        // Store in appropriate column based on type
        //        switch (sharetypeCategory)
        //        {
        //            case "REGISTRATION_FEE":
        //                contribShare.RegFeeAmount = contributionDto.Amount;
        //                _logger.LogInformation($"✓ Contribution stored as REGISTRATION FEE: {contributionDto.Amount:C}");
        //                break;

        //            case "DEPOSIT":
        //                contribShare.DepositsAmount = contributionDto.Amount;
        //                _logger.LogInformation($"✓ Contribution stored as DEPOSIT/SAVINGS: {contributionDto.Amount:C}");
        //                break;

        //            case "DONOR":
        //                contribShare.Donor = contributionDto.Amount;
        //                _logger.LogInformation($"✓ Contribution stored as DONOR/GIFT: {contributionDto.Amount:C}");
        //                break;

        //            case "LOAN_REPAYMENT":
        //                contribShare.LoanAmount = contributionDto.Amount;
        //                _logger.LogInformation($"✓ Contribution stored as LOAN REPAYMENT: {contributionDto.Amount:C}");
        //                break;

        //            case "PASSBOOK":
        //                contribShare.PassBookAmount = contributionDto.Amount;
        //                _logger.LogInformation($"✓ Contribution stored as PASSBOOK: {contributionDto.Amount:C}");
        //                if (shareType.Issharecapital == 1)
        //                {
        //                    await UpdateShareBalanceAsync(contributionDto.MemberNo,
        //                        contributionDto.SharesCode,
        //                        contributionDto.Amount,
        //                        contributionDto.CompanyCode);
        //                }
        //                break;

        //            case "SHARE_CAPITAL":
        //            default:
        //                contribShare.ShareCapitalAmount = contributionDto.Amount;
        //                _logger.LogInformation($"✓ Contribution stored as SHARE CAPITAL: {contributionDto.Amount:C}");
        //                await UpdateShareBalanceAsync(contributionDto.MemberNo,
        //                    contributionDto.SharesCode,
        //                    contributionDto.Amount,
        //                    contributionDto.CompanyCode);
        //                break;
        //        }

        //        _context.ContribShares.Add(contribShare);
        //        await _context.SaveChangesAsync();

        //        // ============================================================
        //        // CREATE BLOCK AND BLOCKCHAIN TRANSACTION (Like Repayment does)
        //        // ============================================================

        //        // Generate block hash
        //        string blockHash = Guid.NewGuid().ToString().Replace("-", "");
        //        if (blockHash.Length < 64) blockHash = blockHash.PadRight(64, '0');
        //        else if (blockHash.Length > 64) blockHash = blockHash.Substring(0, 64);

        //        // Create Block record
        //        var block = new Block
        //        {
        //            BlockHash = blockHash,
        //            PreviousHash = await GetLastBlockHashAsync(),
        //            Timestamp = DateTime.Now,
        //            Nonce = 0,
        //            MerkleRoot = Guid.NewGuid().ToString(),
        //            Confirmed = true,
        //            CreatedAt = DateTime.Now
        //        };

        //        _context.Blocks.Add(block);
        //        await _context.SaveChangesAsync();

        //        // Prepare blockchain data
        //        var blockchainData = new
        //        {
        //            TransactionType = "CONTRIBUTION",
        //            MemberNo = contributionDto.MemberNo,
        //            MemberName = $"{member.Surname} {member.OtherNames}",
        //            ShareType = shareType.SharesType,
        //            ShareTypeCode = shareType.SharesCode,
        //            ContributionCategory = sharetypeCategory,
        //            Amount = contributionDto.Amount,
        //            ReceiptNo = receiptNo,
        //            TransactionDate = contributionDto.TransactionDate.ToString("yyyy-MM-dd HH:mm:ss"),
        //            PaymentMethod = paymentMethod,
        //            ReferenceNo = contributionDto.ReferenceNo,
        //            Remarks = contributionDto.Remarks,
        //            CompanyCode = contributionDto.CompanyCode,
        //            CreatedBy = contributionDto.CreatedBy,
        //            DrAccount = drAcc,
        //            CrAccount = crAcc,
        //            BlockHash = blockHash
        //        };

        //        _logger.LogInformation($"Creating blockchain transaction for contribution: {receiptNo}");

        //        // Create Blockchain Transaction
        //        var blockchainTx = new BlockchainTransaction
        //        {
        //            TransactionId = Guid.NewGuid().ToString(),
        //            TransactionType = "CONTRIBUTION",
        //            MemberNo = contributionDto.MemberNo,
        //            CompanyCode = contributionDto.CompanyCode,
        //            Amount = contributionDto.Amount,
        //            Timestamp = DateTime.Now,
        //            DataHash = await _blockchainService.GenerateTransactionHash(blockchainData),
        //            PayloadJson = System.Text.Json.JsonSerializer.Serialize(blockchainData),
        //            OffChainReferenceId = receiptNo,
        //            Status = "CONFIRMED",
        //            BlockHash = block.BlockHash,
        //            CreatedAt = DateTime.Now
        //        };

        //        _context.BlockchainTransactions.Add(blockchainTx);
        //        await _context.SaveChangesAsync();

        //        // Update records with BlockchainTxId
        //        contrib.BlockchainTxId = blockchainTx.TransactionId;
        //        contribShare.BlockchainTxId = blockchainTx.TransactionId;
        //        glTransaction.BlockchainTxId = blockchainTx.TransactionId;
        //        await _context.SaveChangesAsync();



        //        //// Create blockchain transaction
        //        //var blockchainData = new
        //        //{
        //        //    MemberNo = contributionDto.MemberNo,
        //        //    MemberName = $"{member.Surname} {member.OtherNames}",
        //        //    TransactionType = "CONTRIBUTION",
        //        //    ShareType = shareType.SharesType,
        //        //    ShareTypeCode = shareType.SharesCode,
        //        //    ContributionCategory = sharetypeCategory,
        //        //    Amount = contributionDto.Amount,
        //        //    ReceiptNo = receiptNo,
        //        //    TransactionDate = contributionDto.TransactionDate.ToString("yyyy-MM-dd HH:mm:ss"),
        //        //    PaymentMethod = paymentMethod,
        //        //    ReferenceNo = contributionDto.ReferenceNo,
        //        //    Remarks = contributionDto.Remarks,
        //        //    CompanyCode = contributionDto.CompanyCode,
        //        //    CreatedBy = contributionDto.CreatedBy,
        //        //    DrAccount = drAcc,
        //        //    CrAccount = crAcc
        //        //};

        //        //_logger.LogInformation($"Creating blockchain transaction for contribution: {receiptNo}");

        //        //var blockchainTx = await _blockchainService.CreateAndAddTransactionAsync(
        //        //    "CONTRIBUTION",
        //        //    contributionDto.MemberNo,
        //        //    contributionDto.CompanyCode,
        //        //    contributionDto.Amount,
        //        //    receiptNo,
        //        //    blockchainData
        //        //);

        //        //// Update blockchain transaction ID
        //        //if (blockchainTx != null)
        //        //{
        //        //    contrib.BlockchainTxId = blockchainTx.TransactionId;
        //        //    contribShare.BlockchainTxId = blockchainTx.TransactionId;
        //        //    glTransaction.BlockchainTxId = blockchainTx.TransactionId;
        //        //    await _context.SaveChangesAsync();
        //        //}

        //        await transaction.CommitAsync();

        //        _logger.LogInformation($"Contribution {receiptNo} added successfully for member {contributionDto.MemberNo}");

        //        // Get current share balance (only from share capital)
        //        var shareBalance = await GetMemberShareBalanceAsync(contributionDto.MemberNo);

        //        return new ContributionResponseDTO
        //        {
        //            Id = contrib.Id,
        //            MemberNo = contributionDto.MemberNo,
        //            MemberName = $"{member.Surname} {member.OtherNames}",
        //            TransactionDate = contributionDto.TransactionDate,
        //            SharesCode = contributionDto.SharesCode,
        //            ShareTypeName = shareType.SharesType ?? shareType.SharesCode,
        //            Amount = contributionDto.Amount,
        //            ShareCapitalAmount = contribShare.ShareCapitalAmount ?? 0,
        //            DepositsAmount = contribShare.DepositsAmount ?? 0,
        //            RegFeeAmount = contribShare.RegFeeAmount ?? 0,
        //            Donor = contribShare.Donor ?? 0,
        //            LoanAmount = contribShare.LoanAmount ?? 0,
        //            PassBookAmount = contribShare.PassBookAmount ?? 0,
        //            TotalSharesAfter = shareBalance,
        //            ReceiptNo = receiptNo,
        //            Remarks = contributionDto.Remarks ?? string.Empty,
        //            BlockchainTxId = contrib.BlockchainTxId ?? string.Empty,
        //            CreatedAt = DateTime.Now,
        //            CreatedBy = contributionDto.CreatedBy,
        //            CompanyCode = contributionDto.CompanyCode
        //        };
        //    }
        //    catch (Exception ex)
        //    {
        //        await transaction.RollbackAsync();
        //        _logger.LogError(ex, "Transaction rolled back due to error");

        //        if (ex is ValidationException)
        //        {
        //            throw new Exception($"Validation error: {ex.Message}");
        //        }

        //        throw new Exception($"Error adding contribution: {ex.Message}");
        //    }
        //}

        private async Task<string> GetLastBlockHashAsync()
        {
            try
            {
                var lastBlock = await _context.Blocks
                    .OrderByDescending(b => b.BlockId)
                    .FirstOrDefaultAsync();

                return lastBlock?.BlockHash ?? "0".PadLeft(64, '0');
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error getting last block hash, using default");
                return "0".PadLeft(64, '0');
            }
        }


        /// <summary>
        /// Get default debit account from GlSetup (usually Cash or Bank account)
        /// </summary>
        private string GetDefaultDrAccount(string companyCode)
        {
            // Try to find a default Cash account first
            var cashAccount = _context.GlSetup
                .FirstOrDefault(g => g.CompanyCode == companyCode &&
                                    g.Status == true &&
                                    (g.Glaccname != null && g.Glaccname.ToLower().Contains("cash")) &&
                                    g.Type == "ASSET");

            if (cashAccount != null)
            {
                return cashAccount.AccNo;
            }

            // If no cash account, try any asset account
            var assetAccount = _context.GlSetup
                .FirstOrDefault(g => g.CompanyCode == companyCode &&
                                    g.Status == true &&
                                    g.Type == "ASSET");

            if (assetAccount != null)
            {
                return assetAccount.AccNo;
            }

            // Last resort - return a placeholder (but this will fail validation)
            throw new Exception($"No valid debit account found in GL Setup for company {companyCode}. Please configure a cash or asset account.");
        }

        /// <summary>
        /// Validates if a GL account exists in the system
        /// </summary>
        private async Task<bool> ValidateGlAccountExistsAsync(string accountNo, string companyCode)
        {
            return await _context.GlSetup
                .AnyAsync(g => g.AccNo == accountNo &&
                              g.CompanyCode == companyCode &&
                              g.Status == true);
        }

        public async Task<List<GlSetup>> GetGlAccountsAsync(string companyCode)
        {
            return await _context.GlSetup
                .Where(g => g.CompanyCode == companyCode && g.Status == true)
                .ToListAsync();
        }


        private string DetermineContributionType(Sharetype shareType, ContributionDTO contributionDto)
        {
            // Get all searchable text
            var shareTypeName = (shareType.SharesType ?? shareType.SharesCode ?? "").ToLower();
            var shareTypeCode = (shareType.SharesCode ?? "").ToLower();
            var remarks = (contributionDto.Remarks ?? "").ToLower();
            var referenceNo = (contributionDto.ReferenceNo ?? "").ToLower();
            var paymentMethod = (contributionDto.PaymentMethod ?? "").ToLower();

            _logger.LogDebug($"=== Determining Contribution Type (Word Priority Mode) ===");
            _logger.LogDebug($"ShareType Name: '{shareType.SharesType}'");
            _logger.LogDebug($"ShareType Code: '{shareType.SharesCode}'");
            _logger.LogDebug($"ShareType Flags: IsMainShares={shareType.IsMainShares}, UsedToGuarantee={shareType.UsedToGuarantee}, UsedToOffset={shareType.UsedToOffset}, Withdrawable={shareType.Withdrawable}, Issharecapital={shareType.Issharecapital}");
            _logger.LogDebug($"Remarks: '{contributionDto.Remarks}'");

            // ============================================================
            // PRIORITY 1: CHECK WORDS IN SHARETYPE NAME (HIGHEST PRIORITY)
            // This overrides ANY boolean flag configuration
            // ============================================================

            // 1.1 Check for DEPOSIT/SAVINGS words in ShareType name
            string[] depositKeywords = {
        "deposit", "savings", "saving", "deposits", "share deposit",
        "voluntary", "welfare", "emergency", "flexible"
    };

            foreach (var keyword in depositKeywords)
            {
                if (shareTypeName.Contains(keyword))
                {
                    _logger.LogInformation($"✓ WORD MATCH (ShareType Name): DEPOSIT - matched '{keyword}' in '{shareType.SharesType}'");
                    return "DEPOSIT";
                }
            }

            // 1.2 Check for REGISTRATION FEE words in ShareType name
            string[] regFeeKeywords = {
        "reg fee", "reg fees", "registration", "registration fee", "entry fee",
        "joining fee", "admin fee", "processing fee", "fee", "annual fee",
        "membership fee", "initiation fee", "signup fee", "reg_fee",
        "regfee", "registration_fee", "member_fee"
    };

            foreach (var keyword in regFeeKeywords)
            {
                if (shareTypeName.Contains(keyword))
                {
                    _logger.LogInformation($"✓ WORD MATCH (ShareType Name): REGISTRATION_FEE - matched '{keyword}' in '{shareType.SharesType}'");
                    return "REGISTRATION_FEE";
                }
            }

            // 1.3 Check for DONOR/GIFT words in ShareType name
            string[] donorKeywords = {
        "donor", "donation", "gift", "grant", "sponsor", "endowment", "charity"
    };

            foreach (var keyword in donorKeywords)
            {
                if (shareTypeName.Contains(keyword))
                {
                    _logger.LogInformation($"✓ WORD MATCH (ShareType Name): DONOR - matched '{keyword}' in '{shareType.SharesType}'");
                    return "DONOR";
                }
            }

            // 1.4 Check for LOAN REPAYMENT words in ShareType name
            string[] loanKeywords = {
        "loan", "repayment", "installment", "emi", "loan recovery"
    };

            foreach (var keyword in loanKeywords)
            {
                if (shareTypeName.Contains(keyword))
                {
                    _logger.LogInformation($"✓ WORD MATCH (ShareType Name): LOAN_REPAYMENT - matched '{keyword}' in '{shareType.SharesType}'");
                    return "LOAN_REPAYMENT";
                }
            }

            // 1.5 Check for PASSBOOK words in ShareType name
            string[] passbookKeywords = {
        "passbook", "pass book", "ledger", "pass_book"
    };

            foreach (var keyword in passbookKeywords)
            {
                if (shareTypeName.Contains(keyword))
                {
                    _logger.LogInformation($"✓ WORD MATCH (ShareType Name): PASSBOOK - matched '{keyword}' in '{shareType.SharesType}'");
                    return "PASSBOOK";
                }
            }

            // 1.6 Check for SHARE CAPITAL words in ShareType name
            string[] shareCapitalKeywords = {
        "share capital","share capital", "share", "shares", "capital", "main shares", "equity",
        "core shares", "compulsory shares", "membership shares"
    };

            foreach (var keyword in shareCapitalKeywords)
            {
                if (shareTypeName.Contains(keyword))
                {
                    _logger.LogInformation($"✓ WORD MATCH (ShareType Name): SHARE_CAPITAL - matched '{keyword}' in '{shareType.SharesType}'");
                    return "SHARE_CAPITAL";
                }
            }

            // ============================================================
            // PRIORITY 2: CHECK WORDS IN REMARKS FIELD (User-specified)
            // ============================================================

            // 2.1 Check for DEPOSIT words in Remarks
            foreach (var keyword in depositKeywords)
            {
                if (remarks.Contains(keyword))
                {
                    _logger.LogInformation($"✓ WORD MATCH (Remarks): DEPOSIT - matched '{keyword}' in remarks: '{contributionDto.Remarks}'");
                    return "DEPOSIT";
                }
            }

            // 2.2 Check for REGISTRATION FEE words in Remarks
            foreach (var keyword in regFeeKeywords)
            {
                if (remarks.Contains(keyword))
                {
                    _logger.LogInformation($"✓ WORD MATCH (Remarks): REGISTRATION_FEE - matched '{keyword}' in remarks: '{contributionDto.Remarks}'");
                    return "REGISTRATION_FEE";
                }
            }

            // 2.3 Check for DONOR words in Remarks
            foreach (var keyword in donorKeywords)
            {
                if (remarks.Contains(keyword))
                {
                    _logger.LogInformation($"✓ WORD MATCH (Remarks): DONOR - matched '{keyword}' in remarks: '{contributionDto.Remarks}'");
                    return "DONOR";
                }
            }

            // 2.4 Check for LOAN words in Remarks
            foreach (var keyword in loanKeywords)
            {
                if (remarks.Contains(keyword))
                {
                    _logger.LogInformation($"✓ WORD MATCH (Remarks): LOAN_REPAYMENT - matched '{keyword}' in remarks: '{contributionDto.Remarks}'");
                    return "LOAN_REPAYMENT";
                }
            }

            // 2.5 Check for PASSBOOK words in Remarks
            foreach (var keyword in passbookKeywords)
            {
                if (remarks.Contains(keyword))
                {
                    _logger.LogInformation($"✓ WORD MATCH (Remarks): PASSBOOK - matched '{keyword}' in remarks: '{contributionDto.Remarks}'");
                    return "PASSBOOK";
                }
            }

            // 2.6 Check for SHARE CAPITAL words in Remarks
            foreach (var keyword in shareCapitalKeywords)
            {
                if (remarks.Contains(keyword))
                {
                    _logger.LogInformation($"✓ WORD MATCH (Remarks): SHARE_CAPITAL - matched '{keyword}' in remarks: '{contributionDto.Remarks}'");
                    return "SHARE_CAPITAL";
                }
            }

            // ============================================================
            // PRIORITY 3: CHECK WORDS IN SHARETYPE CODE (Fallback for codes)
            // ============================================================

            foreach (var keyword in depositKeywords)
            {
                if (shareTypeCode.Contains(keyword))
                {
                    _logger.LogInformation($"✓ WORD MATCH (ShareType Code): DEPOSIT - matched '{keyword}' in code: '{shareType.SharesCode}'");
                    return "DEPOSIT";
                }
            }

            foreach (var keyword in regFeeKeywords)
            {
                if (shareTypeCode.Contains(keyword))
                {
                    _logger.LogInformation($"✓ WORD MATCH (ShareType Code): REGISTRATION_FEE - matched '{keyword}' in code: '{shareType.SharesCode}'");
                    return "REGISTRATION_FEE";
                }
            }

            // ============================================================
            // PRIORITY 4: CHECK BOOLEAN FLAGS (Fallback - only if no words matched)
            // Your database flags are respected here, but only as last resort
            // ============================================================

            _logger.LogDebug($"No word matches found, falling back to boolean flags...");

            // 4.1 DEPOSIT/SAVINGS based on flags: Withdrawable=true AND (UsedToGuarantee=true OR UsedToOffset=true)
            if (shareType.Withdrawable == true && (shareType.UsedToGuarantee == true || shareType.UsedToOffset == true))
            {
                _logger.LogInformation($"✓ BOOLEAN FALLBACK: DEPOSIT (Withdrawable={shareType.Withdrawable}, UsedToGuarantee={shareType.UsedToGuarantee}, UsedToOffset={shareType.UsedToOffset})");
                return "DEPOSIT";
            }

            // 4.2 REGISTRATION FEE based on flags
            if (shareType.Issharecapital == 0 &&
                shareType.UsedToGuarantee == false &&
                shareType.UsedToOffset == false &&
                shareType.Withdrawable == false)
            {
                _logger.LogInformation($"✓ BOOLEAN FALLBACK: REGISTRATION_FEE");
                return "REGISTRATION_FEE";
            }

            // 4.3 SHARE CAPITAL based on flags
            if (shareType.IsMainShares == true || shareType.Issharecapital == 1)
            {
                _logger.LogInformation($"✓ BOOLEAN FALLBACK: SHARE_CAPITAL (IsMainShares={shareType.IsMainShares}, Issharecapital={shareType.Issharecapital})");
                return "SHARE_CAPITAL";
            }

            // ============================================================
            // PRIORITY 5: CHECK PAYMENT METHOD & REFERENCE
            // ============================================================

            if (paymentMethod == "loan" || paymentMethod == "installment" || referenceNo.Contains("loan"))
            {
                _logger.LogInformation($"✓ PAYMENT METHOD FALLBACK: LOAN_REPAYMENT");
                return "LOAN_REPAYMENT";
            }

            if (paymentMethod == "donation" || paymentMethod == "grant")
            {
                _logger.LogInformation($"✓ PAYMENT METHOD FALLBACK: DONOR");
                return "DONOR";
            }

            // ============================================================
            // PRIORITY 6: DEFAULT TO SHARE CAPITAL
            // ============================================================
            _logger.LogWarning($"⚠ No match found for ShareType '{shareType.SharesType}' ({shareType.SharesCode}), defaulting to SHARE_CAPITAL");
            return "SHARE_CAPITAL";
        }

        private async Task UpdateShareBalanceAsync(string memberNo, string sharesCode, decimal amount, string companyCode)
        {
            // Find existing share record
            var existingShare = await _context.Shares
                .FirstOrDefaultAsync(s => s.MemberNo == memberNo &&
                                         s.Sharescode == sharesCode &&
                                         s.CompanyCode == companyCode);

            if (existingShare != null)
            {
                // Update existing share
                existingShare.TotalShares = (existingShare.TotalShares ?? 0) + amount;
                existingShare.TransDate = DateTime.Now;
                existingShare.AuditTime = DateTime.Now;
                existingShare.AuditDateTime = DateTime.Now;

                _logger.LogDebug($"Updated share balance for {memberNo} - {sharesCode}: +{amount:C}, New Total: {existingShare.TotalShares:C}");
            }
            else
            {
                // Create new share record
                var newShare = new Share
                {
                    MemberNo = memberNo,
                    Sharescode = sharesCode,
                    TotalShares = amount,
                    Initshares = amount,
                    CompanyCode = companyCode,
                    TransDate = DateTime.Now,
                    AuditTime = DateTime.Now,
                    AuditDateTime = DateTime.Now,
                    AuditId = "SYSTEM"
                };

                _context.Shares.Add(newShare);
                _logger.LogDebug($"Created new share record for {memberNo} - {sharesCode} with amount: {amount:C}");
            }
        }

        private string GenerateReceiptNumber(string companyCode)
        {
            var now = DateTime.Now;
            var day = now.ToString("dd");      
            var month = now.ToString("MM");    
            var hour = now.ToString("HH");     
            var minute = now.ToString("mm");   
            var second = now.ToString("ss"); 
            var receiptNumber = $"REC{month}{day}{hour}{minute}{second.Substring(0, 1)}";

            var random = new Random();
            var existingReceipt = _context.Contribs
                .FirstOrDefault(c => c.ReceiptNo == receiptNumber);

            if (existingReceipt != null)
            {
                // Add a suffix if duplicate occurs
                receiptNumber = $"REC{month}{day}{hour}{minute}{second.Substring(0, 1)}{random.Next(0, 9)}";
                receiptNumber = receiptNumber.Length > 12 ? receiptNumber.Substring(0, 12) : receiptNumber;
            }

            return receiptNumber;
        }

        public async Task<List<ContributionResponseDTO>> GetMemberContributionsAsync(string memberNo)
        {
            var contributions = await _context.Contribs
                .Include(c => c.SharescodeNavigation)
                .Where(c => c.MemberNo == memberNo)
                .OrderByDescending(c => c.ContrDate)
                .Take(100)
                .ToListAsync();

            var member = await _context.Members
                .FirstOrDefaultAsync(m => m.MemberNo == memberNo);

            return contributions.Select(c => new ContributionResponseDTO
            {
                Id = c.Id,
                MemberNo = c.MemberNo,
                MemberName = member != null ? $"{member.Surname} {member.OtherNames}" : c.MemberNo,
                TransactionDate = c.ContrDate ?? DateTime.MinValue,
                SharesCode = c.Sharescode ?? string.Empty,
                ShareTypeName = c.SharescodeNavigation?.SharesType ?? c.Sharescode ?? "Unknown",
                Amount = c.Amount ?? 0,
                ReceiptNo = c.ReceiptNo ?? string.Empty,
                Remarks = c.Remarks ?? string.Empty,
                BlockchainTxId = c.BlockchainTxId ?? string.Empty,
                CreatedAt = c.AuditTime,
                CreatedBy = c.AuditId ?? string.Empty,
                CompanyCode = c.CompanyCode ?? string.Empty
            }).ToList();
        }

        public async Task<List<ShareTypeDTO>> GetShareTypesAsync(string companyCode)
        {
            return await _context.Sharetypes
                .Where(st => st.CompanyCode == companyCode)
                .OrderBy(st => st.Priority)
                .Select(st => new ShareTypeDTO
                {
                    SharesCode = st.SharesCode,
                    SharesType = st.SharesType ?? st.SharesCode,
                    SharesAcc = st.SharesAcc,
                    IsMainShares = st.IsMainShares,
                    UsedToGuarantee = st.UsedToGuarantee,
                    Withdrawable = st.Withdrawable,
                    MinAmount = st.MinAmount,
                    MaxAmount = st.MaxAmount ?? 0,
                    CompanyCode = st.CompanyCode ?? companyCode
                })
                .ToListAsync();
        }

        public async Task<MemberContributionHistoryDTO> GetMemberContributionHistoryAsync(string memberNo)
        {
            var member = await _context.Members
                .FirstOrDefaultAsync(m => m.MemberNo == memberNo);

            if (member == null)
            {
                throw new ValidationException($"Member {memberNo} not found");
            }

            var contributions = await GetMemberContributionsAsync(memberNo);
            var shareBalance = await GetMemberShareBalanceAsync(memberNo);

            return new MemberContributionHistoryDTO
            {
                MemberNo = memberNo,
                MemberName = $"{member.Surname} {member.OtherNames}",
                Contributions = contributions.Select(c => new ContributionDetailDTO
                {
                    TransactionDate = c.TransactionDate,
                    SharesCode = c.SharesCode,
                    ShareTypeName = c.ShareTypeName,
                    Amount = c.Amount,
                    ReceiptNo = c.ReceiptNo,
                    Remarks = c.Remarks,
                    BlockchainTxId = c.BlockchainTxId,
                    CreatedBy = c.CreatedBy
                }).ToList(),
                TotalContributions = contributions.Sum(c => c.Amount),
                CurrentShareBalance = shareBalance,
                CompanyCode = member.CompanyCode ?? string.Empty
            };
        }

        public async Task<List<ContributionResponseDTO>> SearchContributionsAsync(
    DateTime? fromDate,
    DateTime? toDate,
    string? memberNo = null,
    string? shareType = null)
        {
            var query = _context.Contribs.AsQueryable();

            // Apply filters
            if (fromDate.HasValue)
            {
                query = query.Where(c => c.ContrDate >= fromDate);
            }

            if (toDate.HasValue)
            {
                query = query.Where(c => c.ContrDate <= toDate);
            }

            if (!string.IsNullOrEmpty(memberNo))
            {
                query = query.Where(c => c.MemberNo.Contains(memberNo));
            }

            if (!string.IsNullOrEmpty(shareType))
            {
                query = query.Where(c => c.Sharescode == shareType);
            }

            // Execute query
            var contributions = await query
                .OrderByDescending(c => c.ContrDate)
                .Take(200)
                .ToListAsync();

            // Manually get member names for each contribution
            var result = new List<ContributionResponseDTO>();
            foreach (var c in contributions)
            {
                var member = await _context.Members
                    .FirstOrDefaultAsync(m => m.MemberNo == c.MemberNo && m.CompanyCode == c.CompanyCode);

                var shareTypeObj = await _context.Sharetypes
                    .FirstOrDefaultAsync(s => s.SharesCode == c.Sharescode && s.CompanyCode == c.CompanyCode);

                result.Add(new ContributionResponseDTO
                {
                    Id = c.Id,
                    MemberNo = c.MemberNo ?? string.Empty,
                    MemberName = member != null ? $"{member.Surname} {member.OtherNames}".Trim() : c.MemberNo ?? "Unknown",
                    TransactionDate = c.ContrDate ?? DateTime.MinValue,
                    SharesCode = c.Sharescode ?? string.Empty,
                    ShareTypeName = shareTypeObj?.SharesType ?? c.Sharescode ?? "Unknown",
                    Amount = c.Amount ?? 0,
                    ReceiptNo = c.ReceiptNo ?? string.Empty,
                    Remarks = c.Remarks ?? string.Empty,
                    BlockchainTxId = c.BlockchainTxId ?? string.Empty,
                    CreatedAt = c.AuditTime,
                    CreatedBy = c.AuditId ?? string.Empty,
                    CompanyCode = c.CompanyCode ?? string.Empty
                });
            }

            return result;
        }

        public Task<MemberDTO> GetMemberDetailsAsync(string memberNo)
        {
            throw new NotImplementedException();
        }

        //public async Task<ContributionResponseDTO> AddContributionAsync(ContributionDTO contributionDto)
        //{
        //    using var transaction = await _context.Database.BeginTransactionAsync();

        //    try
        //    {
        //        // 1. Validate Member
        //        var member = await _context.Members
        //            .FirstOrDefaultAsync(m => m.MemberNo == contributionDto.MemberNo &&
        //                                      m.CompanyCode == contributionDto.CompanyCode);

        //        if (member == null)
        //            throw new Exception("Member not found");

        //        // 2. Validate Share Type
        //        var shareType = await _context.Sharetypes
        //            .FirstOrDefaultAsync(s => s.SharesCode == contributionDto.SharesCode &&
        //                                     s.CompanyCode == contributionDto.CompanyCode);

        //        if (shareType == null)
        //            throw new Exception("Share type not found");

        //        // 3. Generate Receipt
        //        var receiptNo = contributionDto.ReceiptNo ?? GenerateReceiptNumber(contributionDto.CompanyCode);

        //        // 4. Create Contribution
        //        var contrib = new Contrib
        //        {
        //            MemberNo = contributionDto.MemberNo,
        //            Amount = contributionDto.Amount,
        //            ContrDate = contributionDto.TransactionDate,
        //            CompanyCode = contributionDto.CompanyCode,
        //            ReceiptNo = receiptNo,
        //            Sharescode = contributionDto.SharesCode,
        //            TransactionNo = Guid.NewGuid().ToString().Substring(0, 20),
        //            Posted = "Y",
        //            Locked = "N",
        //            TransDate = contributionDto.TransactionDate,
        //            SharesAcc = shareType.SharesAcc,
        //            ContraAcc = shareType.ContraAcc,
        //            AuditId = contributionDto.CreatedBy,
        //            AuditTime = DateTime.Now,
        //            AuditDateTime = DateTime.Now
        //        };

        //        _context.Contribs.Add(contrib);

        //        // 5. Create Contribution Share
        //        var contribShare = new ContribShare
        //        {
        //            MemberNo = contributionDto.MemberNo,
        //            CompanyCode = contributionDto.CompanyCode,
        //            ReceiptNo = receiptNo,
        //            Sharescode = contributionDto.SharesCode,
        //            TransactionNo = contrib.TransactionNo,
        //            ContrDate = contributionDto.TransactionDate,
        //            ShareCapitalAmount = contributionDto.Amount // simplified
        //        };

        //        _context.ContribShares.Add(contribShare);

        //        await _context.SaveChangesAsync();

        //        // 6. GL Posting
        //        var gl = new Gltransaction
        //        {
        //            TransDate = contributionDto.TransactionDate,
        //            Amount = contributionDto.Amount,

        //            DrAccNo = shareType.ContraAcc, // Cash/Bank
        //            CrAccNo = shareType.SharesAcc, // Shares

        //            DocumentNo = receiptNo,
        //            Source = "CONTRIBUTION",
        //            CompanyCode = contributionDto.CompanyCode,
        //            TransDescript = $"Contribution - {shareType.SharesType} - {contributionDto.MemberNo}",

        //            AuditId = contributionDto.CreatedBy,
        //            AuditTime = DateTime.Now,
        //            AuditDateTime = DateTime.Now,

        //            Cash = contributionDto.PaymentMethod == "CASH" ? 1 : 0,
        //            DocPosted = 1,
        //            TransactionNo = contrib.TransactionNo,
        //            Module = "SHARES"
        //        };

        //        _context.Gltransactions.Add(gl);
        //        await _context.SaveChangesAsync();

        //        await transaction.CommitAsync();

        //        return new ContributionResponseDTO
        //        {
        //            MemberNo = contributionDto.MemberNo,
        //            MemberName = $"{member.Surname} {member.OtherNames}",
        //            Amount = contributionDto.Amount,
        //            ReceiptNo = receiptNo,
        //            SharesCode = contributionDto.SharesCode,
        //            ShareTypeName = shareType.SharesType,
        //            TransactionDate = contributionDto.TransactionDate
        //        };
        //    }
        //    catch (Exception ex)
        //    {
        //        await transaction.RollbackAsync();
        //        throw new Exception($"Error adding contribution: {ex.Message}");
        //    }
        //}
    }
}