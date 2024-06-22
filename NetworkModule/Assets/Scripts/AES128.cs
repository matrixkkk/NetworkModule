using System.Security.Cryptography;
using UnityEngine;
using System;

namespace Assets.Scripts
{
    /// <summary>
    /// ASE 암/복호화 - 128
    /// </summary>
    public class AES128
    {
        // Function to Generate a 64 bits Key.
        public static byte[] GenerateKey()
        {
            RijndaelManaged myRijndael = new RijndaelManaged();

            myRijndael.GenerateKey();
            myRijndael.GenerateIV();

            return myRijndael.Key;
        }
        public static string GenerateBase64Key()
        {
            RijndaelManaged myRijndael = new RijndaelManaged();

            myRijndael.GenerateKey();
            myRijndael.GenerateIV();

            return Convert.ToBase64String(myRijndael.Key);
        }

        //128 암호화
        public static byte[] Encrypt(byte[] input, int size, byte[] key, byte[] iv)
        {
            RijndaelManaged rijndaelCipher = new RijndaelManaged();

            rijndaelCipher.Key = key;
            rijndaelCipher.IV = iv;
            rijndaelCipher.Mode = CipherMode.ECB;
            rijndaelCipher.Padding = PaddingMode.PKCS7;

            ICryptoTransform encryptor = rijndaelCipher.CreateEncryptor();

            return encryptor.TransformFinalBlock(input, 0, size);
        }

        public static byte[] Decrypt(byte[] input, int offset, int size, byte[] key, byte[] iv)
        {
            RijndaelManaged rijndaelCipher = new RijndaelManaged();

            rijndaelCipher.Key = key;
            rijndaelCipher.IV = iv;
            rijndaelCipher.Mode = CipherMode.ECB;
            rijndaelCipher.Padding = PaddingMode.PKCS7;

            if (size < key.Length)
            {
                size = key.Length;
            }

            byte[] resultArray = null;
            try
            {
                ICryptoTransform decryption = rijndaelCipher.CreateDecryptor();
                resultArray = decryption.TransformFinalBlock(input, offset, size);
            }
            catch (CryptographicException e)
            {
                Debug.LogError(e.Message);
            }

            return resultArray;
        }
    }
}