﻿using System.Security.Cryptography;
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
        public static byte[] Encrypt(byte[] input, int size, byte[] key)
        {
            RijndaelManaged RijndaelCipher = new RijndaelManaged();

            RijndaelCipher.Key = key;
            RijndaelCipher.Mode = CipherMode.ECB;
            RijndaelCipher.Padding = PaddingMode.PKCS7;

            ICryptoTransform Encryptor = RijndaelCipher.CreateEncryptor();

            return Encryptor.TransformFinalBlock(input, 0, size);
        }

        public static byte[] Decrypt(byte[] input, int size, byte[] key)
        {
            RijndaelManaged RijndaelCipher = new RijndaelManaged();

            RijndaelCipher.Key = key;
            RijndaelCipher.Mode = CipherMode.ECB;
            RijndaelCipher.Padding = PaddingMode.PKCS7;

            if (size < key.Length)
            {
                size = key.Length;
            }

            byte[] resultArray = null;
#if UNITY_EDITOR
            try
            {
                ICryptoTransform dectryption = RijndaelCipher.CreateDecryptor();
                resultArray = dectryption.TransformFinalBlock(input, 0, size);
            }
            catch (CryptographicException e)
            {
                Debug.LogError(e.Message);
                return null;
            }
#else
        ICryptoTransform dectryption = RijndaelCipher.CreateDecryptor();
        resultArray = dectryption.TransformFinalBlock(input, 0, input.Length);
        if (resultArray == null) return null;
#endif

            return resultArray;
        }
    }
}