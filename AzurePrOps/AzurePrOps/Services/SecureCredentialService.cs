using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using AzurePrOps.Logging;

namespace AzurePrOps.Services;

/// <summary>
/// Provides cross-platform secure storage for sensitive credentials using encrypted file storage
/// </summary>
public class SecureCredentialService
{
    private static readonly ILogger _logger = AppLogger.CreateLogger<SecureCredentialService>();
    private static readonly string CredentialsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AzurePrOps",
        "credentials");
    private const string TokenFileName = "pat.enc";

    /// <summary>
    /// Stores a Personal Access Token securely using cross-platform encryption
    /// </summary>
    /// <param name="token">The PAT token to store</param>
    /// <param name="username">The username/account identifier (unused, kept for compatibility)</param>
    /// <returns>True if the token was stored successfully</returns>
    public bool StorePersonalAccessToken(string token, string username = "AzurePrOps")
    {
        try
        {
            // Ensure the credentials directory exists
            if (!Directory.Exists(CredentialsDirectory))
            {
                Directory.CreateDirectory(CredentialsDirectory);
                
                // Set directory permissions to be more restrictive on Unix-like systems
                if (!OperatingSystem.IsWindows())
                {
                    try
                    {
                        // Set directory permissions to 700 (owner read/write/execute only)
                        File.SetUnixFileMode(CredentialsDirectory, 
                            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to set directory permissions, continuing anyway");
                    }
                }
            }

            var filePath = Path.Combine(CredentialsDirectory, TokenFileName);
            // Always use "AzurePrOps" as entropy for consistency with decryption
            var encryptedData = EncryptToken(token, "AzurePrOps");
            
            File.WriteAllBytes(filePath, encryptedData);
            
            // Set file permissions to be restrictive on Unix-like systems
            if (!OperatingSystem.IsWindows())
            {
                try
                {
                    // Set file permissions to 600 (owner read/write only)
                    File.SetUnixFileMode(filePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to set file permissions, continuing anyway");
                }
            }
            
            // Only log during actual migration or first-time setup, not routine operations
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error storing Personal Access Token securely");
            return false;
        }
    }

    /// <summary>
    /// Retrieves the Personal Access Token from secure storage
    /// </summary>
    /// <returns>The PAT token if found, null otherwise</returns>
    public string? GetPersonalAccessToken()
    {
        try
        {
            var filePath = Path.Combine(CredentialsDirectory, TokenFileName);
            
            if (!File.Exists(filePath))
            {
                _logger.LogDebug("No Personal Access Token file found");
                return null;
            }

            var encryptedData = File.ReadAllBytes(filePath);
            var token = DecryptToken(encryptedData);
            
            if (token == null)
            {
                _logger.LogWarning("Failed to decrypt stored token - token file may be corrupted. Cleaning up corrupted file.");
                // Clean up the corrupted file so the user can log in fresh
                try
                {
                    File.Delete(filePath);
                    _logger.LogInformation("Corrupted token file deleted successfully");
                }
                catch (Exception deleteEx)
                {
                    _logger.LogError(deleteEx, "Failed to delete corrupted token file");
                }
                return null;
            }
            
            _logger.LogDebug("Personal Access Token retrieved from secure storage");
            return token;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving Personal Access Token from secure storage");
            
            // If there's an error reading the file, try to clean it up
            try
            {
                var filePath = Path.Combine(CredentialsDirectory, TokenFileName);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    _logger.LogInformation("Cleaned up potentially corrupted token file after read error");
                }
            }
            catch (Exception cleanupEx)
            {
                _logger.LogError(cleanupEx, "Failed to clean up token file after error");
            }
            
            return null;
        }
    }

    /// <summary>
    /// Removes the Personal Access Token from secure storage
    /// </summary>
    /// <returns>True if the token was removed successfully</returns>
    public bool RemovePersonalAccessToken()
    {
        try
        {
            var filePath = Path.Combine(CredentialsDirectory, TokenFileName);
            
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                _logger.LogInformation("Personal Access Token removed from secure storage");
            }
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing Personal Access Token from secure storage");
            return false;
        }
    }

    /// <summary>
    /// Checks if a Personal Access Token exists in secure storage
    /// </summary>
    /// <returns>True if a token exists</returns>
    public bool HasPersonalAccessToken()
    {
        try
        {
            var filePath = Path.Combine(CredentialsDirectory, TokenFileName);
            return File.Exists(filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking for Personal Access Token in secure storage");
            return false;
        }
    }

    private byte[] EncryptToken(string token, string username)
    {
        var tokenBytes = Encoding.UTF8.GetBytes(token);
        
        if (OperatingSystem.IsWindows())
        {
            // Use Windows Data Protection API (DPAPI) for encryption
            return ProtectedData.Protect(tokenBytes, 
                Encoding.UTF8.GetBytes(username), 
                DataProtectionScope.CurrentUser);
        }
        else
        {
            // For non-Windows platforms, use AES encryption with a machine-specific key
            return EncryptWithAes(tokenBytes, GenerateMachineKey(username));
        }
    }

    private string? DecryptToken(byte[] encryptedData)
    {
        try
        {
            byte[] decryptedBytes;
            
            if (OperatingSystem.IsWindows())
            {
                // Use Windows Data Protection API (DPAPI) for decryption
                // Use the same entropy that was used during encryption
                decryptedBytes = ProtectedData.Unprotect(encryptedData, 
                    Encoding.UTF8.GetBytes("AzurePrOps"), 
                    DataProtectionScope.CurrentUser);
            }
            else
            {
                // For non-Windows platforms, use AES decryption with a machine-specific key
                decryptedBytes = DecryptWithAes(encryptedData, GenerateMachineKey("AzurePrOps"));
            }
            
            return Encoding.UTF8.GetString(decryptedBytes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decrypt token");
            return null;
        }
    }

    private byte[] GenerateMachineKey(string salt)
    {
        // Create a machine-specific key based on hardware and user information
        var machineInfo = new StringBuilder();
        
        // Add machine name
        machineInfo.Append(Environment.MachineName);
        
        // Add user name
        machineInfo.Append(Environment.UserName);
        
        // Add OS version
        machineInfo.Append(Environment.OSVersion.ToString());
        
        // Add application data path (user-specific)
        machineInfo.Append(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
        
        // Add salt
        machineInfo.Append(salt);
        
        // Hash the combined information to create a consistent key
        using var sha256 = SHA256.Create();
        return sha256.ComputeHash(Encoding.UTF8.GetBytes(machineInfo.ToString()));
    }

    private byte[] EncryptWithAes(byte[] data, byte[] key)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.GenerateIV();
        
        using var encryptor = aes.CreateEncryptor();
        using var memoryStream = new MemoryStream();
        
        // Write IV first
        memoryStream.Write(aes.IV, 0, aes.IV.Length);
        
        // Write encrypted data
        using (var cryptoStream = new CryptoStream(memoryStream, encryptor, CryptoStreamMode.Write))
        {
            cryptoStream.Write(data, 0, data.Length);
        }
        
        return memoryStream.ToArray();
    }

    private byte[] DecryptWithAes(byte[] encryptedData, byte[] key)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        
        // Extract IV from the beginning of the encrypted data
        var iv = new byte[aes.IV.Length];
        Array.Copy(encryptedData, 0, iv, 0, iv.Length);
        aes.IV = iv;
        
        using var decryptor = aes.CreateDecryptor();
        using var memoryStream = new MemoryStream(encryptedData, iv.Length, encryptedData.Length - iv.Length);
        using var cryptoStream = new CryptoStream(memoryStream, decryptor, CryptoStreamMode.Read);
        using var resultStream = new MemoryStream();
        
        cryptoStream.CopyTo(resultStream);
        return resultStream.ToArray();
    }
}
