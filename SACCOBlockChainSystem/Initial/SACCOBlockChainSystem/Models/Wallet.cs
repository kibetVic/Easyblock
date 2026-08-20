using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Security.Cryptography;
using System.Text;

namespace SACCOBlockChainSystem.Models
{
    public class Wallet
    {
        [Key]
        [MaxLength(100)]
        public string Address { get; set; }

        [Required]
        public string PublicKey { get; set; }

        // Note: In production, this should be encrypted
        public string? PrivateKeyEncrypted { get; set; }

        [Column(TypeName = "decimal(18,8)")]
        public decimal Balance { get; set; } = 0;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? LastActivity { get; set; }

        // We'll remove navigation properties to avoid circular references
        // Use queries to get transactions instead

        public static Wallet CreateNew()
        {
            using var rsa = RSA.Create(2048);

            var wallet = new Wallet
            {
                PublicKey = Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo()),
                PrivateKeyEncrypted = Convert.ToBase64String(rsa.ExportRSAPrivateKey())
            };

            // Generate address from public key hash
            using var sha256 = SHA256.Create();
            var publicKeyBytes = Encoding.UTF8.GetBytes(wallet.PublicKey);
            var hash = sha256.ComputeHash(publicKeyBytes);

            // Take first 20 bytes for address (like Ethereum)
            var addressBytes = new byte[20];
            Array.Copy(hash, addressBytes, 20);
            wallet.Address = "0x" + Convert.ToHexString(addressBytes).ToLower();

            return wallet;
        }
    }
}