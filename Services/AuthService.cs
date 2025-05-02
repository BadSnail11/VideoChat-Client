using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VideoChat_Client.Models;
using System.Security.Cryptography;
using Supabase;

namespace VideoChat_Client.Services
{
    class AuthService
    {
        private readonly Client _supabase;

        public AuthService(Client supabase)
        {
            _supabase = supabase;
        }

        public async Task<bool> Register(string username, string password)
        {
            // Проверяем, существует ли пользователь
            var existingUser = await _supabase
                .From<Models.User>()
                .Where(x => x.Username == username)
                .Single();

            if (existingUser != null)
                return false;

            // Генерируем соль и хеш пароля
            var passwordHash = HashPassword(password);

            // Создаем нового пользователя
            var newUser = new Models.User
            {
                Username = username,
                PasswordHash = passwordHash
            };

            var response = await _supabase
                .From<Models.User>()
                .Insert(newUser);

            return response.ResponseMessage.IsSuccessStatusCode;
        }
        public async Task<Models.User?> Login(string username, string password)
        {
            // Находим пользователя
            var user = await _supabase
                .From<Models.User>()
                .Where(x => x.Username == username)
                .Single();

            if (user == null)
                return null;

            // Проверяем пароль
            var inputHash = HashPassword(password);
            if (inputHash != user.PasswordHash)
                return null;

            await _supabase
                .From<Models.User>()
                .Where(x => x.Id == user.Id)
                .Set(x => x.IsActive, true)
                .Update();

            // Получаем обновленные данные пользователя
            var updatedUser = await _supabase
                .From<Models.User>()
                .Where(x => x.Id == user.Id)
                .Single();

            return updatedUser;
        }
        private string HashPassword(string password)
        {
            using var sha256 = SHA256.Create();
            var saltedPassword = password;
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(saltedPassword));
            return Convert.ToBase64String(bytes);
        }
    }
}
