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
using System.Data.SqlClient;

namespace PrinterCartridgeApp
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        // ВНИМАНИЕ: Проверь строку подключения!
        // Если у тебя обычный SQL Server (не Express), оставь "localhost".
        // Если Express, замени на @".\SQLEXPRESS"
        string connectionString = @"Data Source=localhost;Initial Catalog=PrinterCartridgeDB;Integrated Security=True;TrustServerCertificate=True;";

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

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                try
                {
                    connection.Open();
                    string query = "SELECT UserID, FullName, RoleID FROM Users WHERE Login = @login AND Password = @password";

                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@login", login);
                        command.Parameters.AddWithValue("@password", password);

                        using (SqlDataReader reader = command.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                string fullName = reader["FullName"].ToString();
                                int roleId = Convert.ToInt32(reader["RoleID"]);

                                MessageBox.Show($"Успешный вход!\nФИО: {fullName}\nРоль (ID): {roleId}", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);

                                // TODO: Позже здесь мы сделаем открытие разных окон в зависимости от RoleID
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
    }
}