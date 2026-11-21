using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SekaiUtau.Core.Collab;

namespace SekaiUtau.App.Views {
    public partial class DuoAccountDialog : Window {
        public DuoAccountDialog() {
            InitializeComponent();
            AccountList.SelectionChanged += (_, _) => {
                if (AccountList.SelectedItem is Account account) {
                    NameBox.Text = account.Name;
                    PasswordBox.Text = string.Empty;
                }
            };
            Refresh();
        }

        void Refresh() {
            var current = Accounts.Current;
            AccountList.ItemsSource = Accounts.All.ToList();
            CurrentText.Text = current == null
                ? ThemeManager.GetString("duo.current.none")
                : string.Format(ThemeManager.GetString("duo.current.signed"), current.Name);
            var signedIn = current != null;
            SignInButton.IsEnabled = true;
            SignOutButton.IsEnabled = signedIn;
            DeleteButton.IsEnabled = Accounts.All.Count > 0;
        }

        void ShowError(string? error) {
            if (error == null) {
                StatusText.IsVisible = false;
                StatusText.Text = string.Empty;
            } else {
                StatusText.Text = ThemeManager.GetString(error);
                StatusText.IsVisible = true;
            }
        }

        void OnCreate(object? sender, RoutedEventArgs args) {
            var error = Accounts.Create(NameBox.Text ?? string.Empty, PasswordBox.Text ?? string.Empty);
            ShowError(error);
            Refresh();
        }

        void OnSignIn(object? sender, RoutedEventArgs args) {
            var error = Accounts.SignIn(NameBox.Text ?? string.Empty, PasswordBox.Text ?? string.Empty);
            ShowError(error);
            Refresh();
        }

        void OnSignOut(object? sender, RoutedEventArgs args) {
            Accounts.SignOut();
            ShowError(null);
            Refresh();
        }

        async void OnDelete(object? sender, RoutedEventArgs args) {
            var name = NameBox.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name)) {
                ShowError("duo.error.unknown");
                return;
            }
            var confirm = await MessageBox.Show(this,
                string.Format(ThemeManager.GetString("duo.delete.confirm"), name),
                ThemeManager.GetString("duo.delete.title"),
                MessageBox.MessageBoxButtons.YesNo);
            if (confirm != MessageBox.MessageBoxResult.Yes) {
                return;
            }
            ShowError(Accounts.Delete(name, PasswordBox.Text ?? string.Empty));
            Refresh();
        }

        void OnClose(object? sender, RoutedEventArgs args) => Close();
    }
}
