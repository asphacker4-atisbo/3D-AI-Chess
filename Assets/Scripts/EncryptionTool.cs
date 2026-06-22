using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

public static class EncryptionTool
{
    private static readonly string key = "@j%n4D04t0]6F>z*"; // Debe tener 16 caracteres

    public static void SaveEncrypted(string json, string path)
    {
        byte[] keyBytes = Encoding.UTF8.GetBytes(key);
        using (Aes aes = Aes.Create())
        {
            aes.Key = keyBytes;
            aes.GenerateIV();
            using (FileStream fs = new FileStream(path, FileMode.Create))
            {
                fs.Write(aes.IV, 0, aes.IV.Length); // Guardamos el IV al inicio
                using (CryptoStream cs = new CryptoStream(fs, aes.CreateEncryptor(), CryptoStreamMode.Write))
                using (StreamWriter sw = new StreamWriter(cs))
                {
                    sw.Write(json);
                }
            }
        }
    }

    public static string LoadDecrypted(string path)
    {
        if (!File.Exists(path)) return null;
        byte[] keyBytes = Encoding.UTF8.GetBytes(key);
        using (Aes aes = Aes.Create())
        {
            aes.Key = keyBytes;
            using (FileStream fs = new FileStream(path, FileMode.Open))
            {
                byte[] iv = new byte[aes.BlockSize / 8];
                fs.Read(iv, 0, iv.Length);
                aes.IV = iv;
                using (CryptoStream cs = new CryptoStream(fs, aes.CreateDecryptor(), CryptoStreamMode.Read))
                using (StreamReader sr = new StreamReader(cs))
                {
                    return sr.ReadToEnd();
                }
            }
        }
    }
}