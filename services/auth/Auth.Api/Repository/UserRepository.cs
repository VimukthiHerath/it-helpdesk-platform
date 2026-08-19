using System.Data;
using Auth.Api.Model;
using MySql.Data.MySqlClient;
using Microsoft.Extensions.Configuration;

namespace Auth.Api.Repository;

public class UserRepository(IConfiguration configuration)
{
    private readonly string _connectionString =  configuration.GetConnectionString("AuthDb")
     ?? throw new InvalidOperationException("Connection string 'AuthDb' not found.");

    // Get all users
    public async Task<List<User>> GetUsersAsync()
    {
        var users = new List<User>();

        using (var connection = new MySqlConnection(_connectionString))
        {
            await connection.OpenAsync();

            const string query = "SELECT Id, Name, Email, Role FROM users";

            using var command = new MySqlCommand(query, connection);
            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var user = new User
                {
                    Id = reader.GetInt32("Id"),
                    Name = reader.GetString("Name"),
                    Email = reader.GetString("Email"),
                    Role = reader.GetString("Role"),
                };
                users.Add(user);
            }
        }
        return users;
    }
    
}