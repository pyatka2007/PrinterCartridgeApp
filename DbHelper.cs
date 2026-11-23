using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;

namespace PrinterCartridgeApp
{
    public static class DbHelper
    {
        public static string ConnectionString = @"Data Source=(localdb)\MSSQLLocalDB;Initial Catalog=PrinterCartridgeDB;Integrated Security=True;TrustServerCertificate=True;";

        // Метод для создания хэша пароля
        public static string HashPassword(string password)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                // Превращаем строку в байты
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));

                // Собираем байты обратно в строку
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < bytes.Length; i++)
                {
                    builder.Append(bytes[i].ToString("x2"));
                }
                return builder.ToString();
            }
        }
    }
}
