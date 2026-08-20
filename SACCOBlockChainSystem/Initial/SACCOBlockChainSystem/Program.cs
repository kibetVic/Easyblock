using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Versioning; 
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SACCOBlockChainSystem.Data;
using SACCOBlockChainSystem.Repositories;
using SACCOBlockChainSystem.Services;
using QuestPDF.Infrastructure;
using Radzen;

var builder = WebApplication.CreateBuilder(args);

QuestPDF.Settings.License = LicenseType.Community;

// Add services
builder.Services.AddControllersWithViews();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();
builder.Services.AddServerSideBlazor();
builder.Services.AddRadzenComponents();
builder.Services.AddSignalR();// For API calls if needed
// session for storing verification codes
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// Add Authentication services
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(2);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Cookie.SameSite = SameSiteMode.Lax;
        //options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        //options.Cookie.SameSite = SameSiteMode.Strict;
    });

builder.Services.AddAuthorization(options =>
{
    // Add role-based policies
    options.AddPolicy("AdminOnly", policy =>
        policy.RequireRole("Admin", "SuperAdmin"));

    options.AddPolicy("MemberOnly", policy =>
        policy.RequireRole("Member", "Admin", "SuperAdmin"));

    options.AddPolicy("RequireAuthenticatedUser", policy =>
        policy.RequireAuthenticatedUser());

    // ADD THIS POLICY - this is what your controller is using
    options.AddPolicy("Admin", policy =>
        policy.RequireRole("Admin", "SuperAdmin"));
});

// Database Context
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("BlockchainDb")));

// Register Repository Pattern
builder.Services.AddScoped<IMemberRepository, MemberRepository>();
builder.Services.AddScoped<ITransactionRepository, TransactionRepository>();

// Register Application Services
//builder.Services.AddScoped<UserGroupService>();
builder.Services.AddScoped<ICollateralService, CollateralService>();
builder.Services.AddScoped<IInquiryService, InquiryService>();
builder.Services.AddScoped<IBlockchainService, BlockchainService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<ISaccoService, SaccoService>();
builder.Services.AddScoped<IMemberService, MemberService>();
builder.Services.AddScoped<ITransactionService, TransactionService>();
builder.Services.AddScoped<ILoanTypeService, LoanTypeService>();
builder.Services.AddScoped<ILoanTypeService, LoanTypeService>();
builder.Services.AddScoped<IShareService, ShareService>();
builder.Services.AddScoped<ICompanyContextService, CompanyContextService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IShareTypeService, ShareTypeService>();
builder.Services.AddScoped<ILoanService, LoanService>();
builder.Services.AddScoped<IBankService, BankService>();
builder.Services.AddScoped<IGlAccountService, GlAccountService>();
builder.Services.AddScoped<ICompanyService, CompanyService>();
builder.Services.AddScoped<IGIGsService, GIGsService>();
builder.Services.AddScoped<ISmsService, SmsService>();
builder.Services.AddScoped<INextOfKeenService, NextOfKeenService>();
builder.Services.AddScoped<IShareTransferService, ShareTransferService>();
builder.Services.AddScoped<IWithdrawalService, WithdrawalService>();
builder.Services.AddScoped<IPortfolioAtRiskService, PortfolioAtRiskService>();
builder.Services.AddScoped<ILoanTypePerformanceService, LoanTypePerformanceService>();
builder.Services.AddScoped<IChequeReceivedReportService, ChequeReceivedReportService>();
builder.Services.AddScoped<AuditTrailService>();
builder.Services.AddScoped<IAgentService, AgentService>();
builder.Services.AddScoped<IReportService, ReportService>();

builder.Services.AddHostedService<BlockchainSyncService>();
builder.Services.AddHostedService<TransactionProcessorService>();
builder.Services.AddHostedService<LoanOverdueUpdateService>();

// email service
builder.Services.AddScoped<IEmailService, EmailService>();

// Caching
builder.Services.AddMemoryCache();

// Add logging
builder.Services.AddLogging(configure =>
    configure.AddConsole().AddDebug().SetMinimumLevel(LogLevel.Information));

// SIMPLIFIED Health Checks (without AddDbContextCheck)
builder.Services.AddHealthChecks()
    .AddCheck("Database", () =>
        HealthCheckResult.Healthy("Database connection is healthy"));

// SIMPLIFIED API Versioning (optional - you can remove if not needed)
builder.Services.AddApiVersioning(options =>
{
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.ReportApiVersions = true;
});

// SIMPLIFIED Swagger/OpenAPI for API documentation (optional - for development)
if (builder.Environment.IsDevelopment())
{
    // Minimal Swagger configuration
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();
}

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();

    // Enable Swagger UI (only if Swagger was configured)
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();
app.MapBlazorHub();
// SIMPLIFIED Health check endpoint
app.MapHealthChecks("/health");

// Map controller routes
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapControllerRoute(
    name: "api",
    pattern: "api/{controller}/{action}/{id?}");

app.Run();