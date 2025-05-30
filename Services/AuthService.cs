﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VideoChat_Client.Models;
using System.Security.Cryptography;
using Supabase;
using System.Diagnostics;

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
            var existingUser = await _supabase
                .From<Models.User>()
                .Where(x => x.Username == username)
                .Single();

            if (existingUser != null)
                return false;

            var passwordHash = HashPassword(password);

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
            var user = await _supabase
                .From<Models.User>()
                .Where(x => x.Username == username)
                .Single();

            if (user == null)
                return null;

            var inputHash = HashPassword(password);
            if (inputHash != user.PasswordHash)
                return null;

            await _supabase
                .From<Models.User>()
                .Where(x => x.Id == user.Id)
                .Set(x => x.IsActive, true)
                .Update();

            var updatedUser = await _supabase
                .From<Models.User>()
                .Where(x => x.Id == user.Id)
                .Single();

            return updatedUser;
        }
        public async Task<bool> Logout(Guid userId)
        {
            try
            {
                var response = await _supabase
                    .From<Models.User>()
                    .Where(x => x.Id == userId)
                    .Set(x => x.IsActive, false)
                    .Set(x => x.LastOnline, DateTime.UtcNow)
                    .Update();

                return response.ResponseMessage.IsSuccessStatusCode;
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("Logout timeout exceeded");
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Logout error: {ex.Message}");
                return false;
            }
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
