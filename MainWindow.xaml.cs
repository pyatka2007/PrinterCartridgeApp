using MaterialDesignThemes.Wpf;
using System.Data.SqlClient;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace PrinterCartridgeApp
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void btnLogin_Click(object sender, RoutedEventArgs e)
        {
            string login = txtLogin.Text.Trim();
            string password = txtPassword.Password.Trim();

            if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(password))
            {
                txtError.Text = "Заполните все поля!";
                txtError.Visibility = Visibility.Visible;
                return;
            }

            // ХЭШИРУЕМ ВВЕДЕННЫЙ ПАРОЛЬ
            string hashedPassword = DbHelper.HashPassword(password);

            using (SqlConnection connection = new SqlConnection(DbHelper.ConnectionString))
            {
                try
                {
                    connection.Open();
                    // Добавили проверку IsActive = 1 (пользователь не заблокирован)
                    string query = "SELECT UserID, FullName, RoleID, IsActive FROM Users WHERE Login = @login AND Password = @password";

                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@login", login);
                        command.Parameters.AddWithValue("@password", hashedPassword); // Передаем хэш!

                        using (SqlDataReader reader = command.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                // Проверяем, не заблокирован ли аккаунт
                                bool isActive = Convert.ToBoolean(reader["IsActive"]);
                                if (!isActive)
                                {
                                    MessageBox.Show("Ваш аккаунт заблокирован администратором!", "Доступ запрещен", MessageBoxButton.OK, MessageBoxImage.Stop);
                                    return;
                                }

                                int userId = Convert.ToInt32(reader["UserID"]);
                                int roleId = Convert.ToInt32(reader["RoleID"]);

                                if (roleId == 1)
                                {
                                    UserWindow userWin = new UserWindow(userId);
                                    userWin.Show();
                                    this.Close();
                                }
                                else if (roleId == 2)
                                {
                                    TechWindow techWin = new TechWindow(userId);
                                    techWin.Show();
                                    this.Close();
                                }
                                else if (roleId == 3)
                                {
                                    AdminWindow adminWin = new AdminWindow(userId);
                                    adminWin.Show();
                                    this.Close();
                                }
                            }
                            else
                            {
                                txtError.Text = "Неверный логин или пароль";
                                txtError.Visibility = Visibility.Visible;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Ошибка подключения к БД: " + ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        private void ThemeToggle_Checked(object sender, RoutedEventArgs e)
        {
            var paletteHelper = new PaletteHelper();
            var theme = paletteHelper.GetTheme();
            theme.SetBaseTheme(BaseTheme.Dark);
            paletteHelper.SetTheme(theme);
            ThemeIcon.Kind = PackIconKind.WeatherSunny;
        }

        private void ThemeToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            var paletteHelper = new PaletteHelper();
            var theme = paletteHelper.GetTheme();
            theme.SetBaseTheme(BaseTheme.Light);
            paletteHelper.SetTheme(theme);
            ThemeIcon.Kind = PackIconKind.WeatherNight;
        }
    }
}