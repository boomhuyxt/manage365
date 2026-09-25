namespace manage365.Routes.API.Auth;

public static class AppRoles
{
    public const string Admin = "Admin";
    public const string Manager = "Manager";
    public const string Employee = "Employee";

    public const string Managers = Admin + "," + Manager;
}
