using System.Configuration;
using System.Data;
using System.Windows;
using VideoChat_Client.Views;

namespace VideoChat_Client;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public static Models.User? CurrentUser { get; set; }
}

