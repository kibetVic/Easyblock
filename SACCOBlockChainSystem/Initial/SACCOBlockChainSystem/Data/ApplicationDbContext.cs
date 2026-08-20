using Microsoft.EntityFrameworkCore;
using SACCOBlockchainDb.Models;
using SACCOBlockChainSystem.Models;
using SACCOBlockChainSystem.Models.ViewModels;
using static SACCOBlockChainSystem.Services.MemberService;

namespace SACCOBlockChainSystem.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }
        public DbSet<UserGroup> UserGroups { get; set; }
        public DbSet<ColloanGuar> ColloanGuars { get; set; }
        public DbSet<Collateral> Collaterals { get; set; }
        public DbSet<Privillage> Privilages { get; set; }
        public DbSet<RolePrivilage> RolePrivileges { get; set; }
        public DbSet<Client> Clients { get; set; }
        public DbSet<Company> Companies { get; set; }
        public DbSet<SaccoParram> SaccoParram { get; set; }
        public DbSet<Contrib> Contribs { get; set; }
        public DbSet<ContribShare> ContribShares { get; set; }
        public DbSet<CoopTransaction> CoopTransactions { get; set; }
        public DbSet<GeneralLedger> GeneralLedgers { get; set; }
        public DbSet<Gltransaction> Gltransactions { get; set; }
        public DbSet<AuditTrail> AuditTrails { get; set; }
        public DbSet<Loantype> Loantypes { get; set; }
        public DbSet<Loan> Loans { get; set; }
        public DbSet<Loanbal> Loanbal { get; set; }
        public DbSet<Loanguar> Loanguar { get; set; }
        public DbSet<Endmain> Endmain { get; set; }
        public DbSet<Appraisal> Appraisal { get; set; }
        public DbSet<Cheque> Cheques { get; set; }
        public DbSet<Repay> Repay { get; set; }
        public DbSet<LoanSchedule> LoanSchedules { get; set; }
        public DbSet<Member> Members { get; set; }
        public DbSet<NextOfKeen> NextOfKeens { get; set; }
        public DbSet<Share> Shares { get; set; }
        public DbSet<Sharetype> Sharetypes { get; set; }
        public DbSet<Transaction> Transactions { get; set; }
        public DbSet<TransactionDetail> TransactionDetails { get; set; }
        public DbSet<Transactions2> Transactions2 { get; set; }
        public DbSet<UserAccounts1> UserAccounts1 { get; set; }
        public DbSet<WicciClient> WicciClients { get; set; }
        public DbSet<Wallet> Wallets { get; set; }
        public DbSet<Agent> Agents { get; set; }
        public DbSet<GlSetup> GlSetup { get; set; }
        public DbSet<GIGs> CIGs { get; set; }
        public DbSet<MemberNumberCounter> MemberNumberCounters { get; set; }
        public DbSet<Block> Blocks { get; set; }
        public DbSet<Journal> Journals { get; set; }
        public DbSet<SmsMessage> SmsMessages { get; set; }
        public DbSet<SmsTemplate> SmsTemplates { get; set; }
        public DbSet<SmsSetting> SmsSettings { get; set; }
        public DbSet<JournalsListing> JournalsListings { get; set; }
        public DbSet<AssetsRegister> AssetsRegisters { get; set; }
        public DbSet<County> Counties { get; set; }
        public DbSet<SubCounty> SubCounties { get; set; }
        public DbSet<Ward> Wards { get; set; }
        public DbSet<BlockchainTransaction> BlockchainTransactions { get; set; }
        public DbSet<MemberWithdrawal> MemberWithdrawals { get; set; }
        public DbSet<WithdrawalApproval> WithdrawalApprovals { get; set; }
        public DbSet<WithdrawalDocument> WithdrawalDocuments { get; set; }
        public DbSet<ShareTransfer> ShareTransfers { get; set; }
        public DbSet<ShareTransferApproval> ShareTransferApprovals { get; set; }
        public DbSet<ShareTransferDocument> ShareTransferDocuments { get; set; }
        public DbSet<AuditLog> AuditLogs { get; set; }
        public DbSet<Bank> Banks { get; set; }
        public DbSet<ApiTransaction> ApiTransactions { get; set; }
        public DbSet<ApiTable> ApiTables { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure primary keys for tables without explicit [Key] attribute
            modelBuilder.Entity<Loantype>().HasKey(l => l.Id);
            modelBuilder.Entity<Share>().HasKey(s => new { s.MemberNo, s.Sharescode });
            modelBuilder.Entity<Transaction>().HasKey(t => t.Id);
            modelBuilder.Entity<TransactionDetail>().HasKey(t => t.Id);
            modelBuilder.Entity<Transactions2>().HasKey(t => t.Id);

            // Configure Contrib to Member relationship
            modelBuilder.Entity<Contrib>(entity =>
            {
                entity.HasOne(c => c.MemberNoNavigation)
                    .WithMany()
                    .HasForeignKey(c => new { c.MemberNo, c.CompanyCode })
                    .HasPrincipalKey(m => new { m.MemberNo, m.CompanyCode })
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // Configure Contrib to Sharetype relationship
            modelBuilder.Entity<Contrib>(entity =>
            {
                entity.HasOne(c => c.SharescodeNavigation)
                    .WithMany(s => s.Contribs)
                    .HasForeignKey(c => c.Sharescode);
            });

            modelBuilder.Entity<LoanSchedule>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.HasOne(e => e.Loan)
                    .WithMany()
                    .HasForeignKey(e => e.LoanNo)
                    .HasPrincipalKey(e => e.LoanNo);
            });

            modelBuilder.Model.GetEntityTypes()
                    .SelectMany(e => e.GetNavigations())
                    .ToList()
                    .ForEach(n => n.SetIsEagerLoaded(false));

            modelBuilder.Entity<Endmain>(entity =>
            {
                entity.ToTable("Endmain");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.LoanNo).IsRequired();
                entity.Property(e => e.CompanyCode).HasMaxLength(50);
            });

            modelBuilder.Entity<Endmain>(entity =>
            {
                entity.ToTable("Endmain");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.LoanNo).IsRequired().HasMaxLength(50);
                entity.Property(e => e.CompanyCode).HasMaxLength(50);
                entity.Property(e => e.MinuteNo).HasMaxLength(20);
                entity.Property(e => e.Accepted).HasMaxLength(3);
                entity.Property(e => e.MembSigned).HasColumnName("MembSigned");
            });

            modelBuilder.Entity<Cheque>()
                .HasOne<Member>()
                .WithMany()
                .HasForeignKey(c => new { c.MemberNo, c.CompanyCode })
                .HasPrincipalKey(m => new { m.MemberNo, m.CompanyCode })
                .OnDelete(DeleteBehavior.Restrict);

            // Transferor
            modelBuilder.Entity<ShareTransfer>()
                .HasOne(st => st.Transferor)
                .WithMany()
                .HasForeignKey(st => new { st.TransferorMemberNo, st.CompanyCode })
                .HasPrincipalKey(m => new { m.MemberNo, m.CompanyCode })
                .OnDelete(DeleteBehavior.Restrict);

            // Transferee
            modelBuilder.Entity<ShareTransfer>()
                .HasOne(st => st.Transferee)
                .WithMany()
                .HasForeignKey(st => new { st.TransfereeMemberNo, st.CompanyCode })
                .HasPrincipalKey(m => new { m.MemberNo, m.CompanyCode })
                .OnDelete(DeleteBehavior.Restrict);

            // ShareType
            modelBuilder.Entity<ShareTransfer>()
                .HasOne(st => st.ShareType)
                .WithMany()
                .HasForeignKey(st => new { st.SharesCode, st.CompanyCode })
                .HasPrincipalKey(s => new { s.SharesCode, s.CompanyCode })
                .OnDelete(DeleteBehavior.Restrict);

            // Configure relationship
            modelBuilder.Entity<MemberWithdrawal>()
                .HasOne(mw => mw.Member)
                .WithMany()
                .HasForeignKey(mw => new { mw.MemberNo, mw.CompanyCode })
                .HasPrincipalKey(m => new { m.MemberNo, m.CompanyCode });

            // Define composite alternate key in Member
            modelBuilder.Entity<Member>()
                 .HasKey(m => new { m.MemberNo, m.CompanyCode });

            // Configure relationship
            modelBuilder.Entity<NextOfKeen>()
                .HasOne(n => n.Member)
                .WithMany(m => m.NextOfKeens)
                .HasForeignKey(n => new { n.MemberNo, n.CompanyCode })
                .HasPrincipalKey(m => new { m.MemberNo, m.CompanyCode });

            // Configure GIGs - Company relationship
            modelBuilder.Entity<GIGs>(entity =>
            {
                // Configure the relationship with Company
                entity.HasOne(g => g.Company)
                    .WithMany() // Company doesn't have a collection of GIGs
                    .HasForeignKey(g => g.CompanyCode)
                    .HasPrincipalKey(c => c.CompanyCode) // Use CompanyCode as the principal key
                    .OnDelete(DeleteBehavior.Restrict);

                // Add unique constraint on GigCode
                entity.HasIndex(g => g.GigCode)
                    .IsUnique();

                // Add index on CompanyCode for better performance
                entity.HasIndex(g => g.CompanyCode);

                // Add index on Status
                entity.HasIndex(g => g.Status);
            });

            modelBuilder.Entity<Member>().Ignore(m => m.Id);

            // Explicitly configure Company entity
            modelBuilder.Entity<Company>(entity =>
            {
                entity.ToTable("Companies");

                entity.HasKey(e => e.Id);

                entity.Property(e => e.CompanyCode)
                    .HasColumnName("CompanyCode")
                    .HasMaxLength(50)
                    .IsRequired();

                entity.Property(e => e.CompanyName)
                    .HasColumnName("CompanyName")
                    .HasMaxLength(200);
            });

            // Configure relationships
            modelBuilder.Entity<Contrib>()
                .HasOne(c => c.SharescodeNavigation)
                .WithMany(s => s.Contribs)
                .HasForeignKey(c => c.Sharescode);

            modelBuilder.Entity<ContribShare>()
                .HasOne(c => c.SharescodeNavigation)
                .WithMany(s => s.ContribShares)
                .HasForeignKey(c => c.Sharescode);

            modelBuilder.Entity<MemberNumberCounter>(entity =>
            {
                entity.HasKey(e => e.CompanyCode);
                entity.Property(e => e.CompanyCode)
                    .HasMaxLength(10)
                    .IsRequired();
                entity.Property(e => e.LastNumber)
                    .IsRequired();
                entity.Property(e => e.LastUpdated)
                    .IsRequired();
            });

            modelBuilder.Entity<Member>(entity =>
            {
                entity.Property(e => e.BlockchainTxId)
                    .HasMaxLength(255)
                    .HasColumnName("BlockchainTxId");
            });

            modelBuilder.Entity<RolePrivilage>()
  .HasKey(rp => new { rp.UserGroupId, rp.PrivilageId });

            modelBuilder.Entity<Loan>()
                .ToTable(tb => tb.UseSqlOutputClause(false));

            // Block - BlockchainTransaction relationship
            modelBuilder.Entity<BlockchainTransaction>()
                .HasOne(t => t.Block)
                .WithMany(b => b.Transactions)
                .HasForeignKey(t => t.BlockHash)
                .HasPrincipalKey(b => b.BlockHash);

            // Configure default for all decimals
            foreach (var property in modelBuilder.Model.GetEntityTypes()
                .SelectMany(t => t.GetProperties())
                .Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
            {
                property.SetColumnType("decimal(18,2)");
            }

            // Override for specific properties that need different precision
            modelBuilder.Entity<Sharetype>(entity =>
            {
                entity.Property(e => e.Interest).HasPrecision(5, 4); // For percentages like 0.1250
                entity.Property(e => e.ElseRatio).HasPrecision(5, 4);
            });

            // Add indexes for performance
            modelBuilder.Entity<Member>().HasIndex(m => m.MemberNo).IsUnique();
            modelBuilder.Entity<Member>().HasIndex(m => m.Idno);
            modelBuilder.Entity<Member>().HasIndex(m => m.PhoneNo);
            modelBuilder.Entity<Member>().HasIndex(m => m.BlockchainTxId);

            modelBuilder.Entity<BlockchainTransaction>().HasIndex(t => t.TransactionId).IsUnique();
            modelBuilder.Entity<BlockchainTransaction>().HasIndex(t => t.MemberNo);
            modelBuilder.Entity<BlockchainTransaction>().HasIndex(t => t.TransactionType);
            modelBuilder.Entity<BlockchainTransaction>().HasIndex(t => t.Timestamp);

            modelBuilder.Entity<Block>().HasIndex(b => b.BlockHash).IsUnique();
            modelBuilder.Entity<Block>().HasIndex(b => b.PreviousHash);

            modelBuilder.Entity<Contrib>().HasIndex(c => c.MemberNo);
            modelBuilder.Entity<Contrib>().HasIndex(c => c.BlockchainTxId);

            modelBuilder.Entity<ContribShare>().HasIndex(c => c.MemberNo);
            modelBuilder.Entity<ContribShare>().HasIndex(c => c.BlockchainTxId);

            modelBuilder.Entity<Transactions2>().HasIndex(t => t.MemberNo);
            modelBuilder.Entity<Transactions2>().HasIndex(t => t.BlockchainTxId);
        }
    }
}