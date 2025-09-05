
using System;
using Play929Backend.Models;
using Play929Backend.DTOs;
namespace Play929Backend.DTOs
{
    /// <summary>
    /// Represents the result of user authentication.
    /// </summary>

public class UserAuthenticationResult
{
    public User User { get; set; }
    public string AccessToken { get; set; }
    // other properties as needed
}
}