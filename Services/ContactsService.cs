using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VideoChat_Client.Models;
using Supabase;

namespace VideoChat_Client.Services
{
    public class ContactsService
    {
        private readonly Client _supabase;

        public ContactsService(Client supabase)
        {
            _supabase = supabase;
        }

        public async Task<List<User>> GetUserContacts(Guid userId)
        {
            try
            {
                var contactsResponse = await _supabase
                    .From<Contact>()
                    .Where(x => x.UserId == userId)
                    .Get();

                if (contactsResponse.Models?.Any() != true)
                    return new List<User>();

                var contactIds = contactsResponse.Models.Select(c => c.ContactId).ToList();

                var usersResponse = await _supabase
                    .From<User>()
                    .Where(x => contactIds.Contains(x.Id))
                    .Get();

                return usersResponse.Models;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting contacts: {ex.Message}");
                return new List<User>();
            }
        }

        public async Task<bool> AddContact(Guid userId, string username)
        {
            try
            {
                var userResponse = await _supabase
                    .From<User>()
                    .Where(x => x.Username == username)
                    .Single();

                if (userResponse == null)
                    return false;

                var existingContact = await _supabase
                    .From<Contact>()
                    .Where(x => x.UserId == userId && x.ContactId == userResponse.Id)
                    .Single();

                if (existingContact != null)
                    return false;

                var newContact = new Contact
                {
                    UserId = userId,
                    ContactId = userResponse.Id,
                    CreatedAt = DateTime.UtcNow
                };

                await _supabase
                    .From<Contact>()
                    .Insert(newContact);

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error adding contact: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> RemoveContact(Guid userId, Guid contactId)
        {
            try
            {
                await _supabase
                    .From<Contact>()
                    .Where(x => x.UserId == userId && x.ContactId == contactId)
                    .Delete();

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error removing contact: {ex.Message}");
                return false;
            }
        }

        public async Task<List<User>> SearchUsers(string searchTerm)
        {
            try
            {
                var response = await _supabase
                    .From<User>()
                    .Where(x => x.Username.Contains(searchTerm))
                    .Get();

                return response.Models;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error searching users: {ex.Message}");
                return new List<User>();
            }
        }
    }
}
