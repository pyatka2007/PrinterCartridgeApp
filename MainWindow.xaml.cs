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

            using (SqlConnection connection = new SqlConnection(DbHelper.ConnectionString))
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
                                int userId = Convert.ToInt32(reader["UserID"]);
                                int roleId = Convert.ToInt32(reader["RoleID"]);

                                // Открываем окно в зависимости от роли
                                if (roleId == 1) // Пользователь
                                {
                                    UserWindow userWin = new UserWindow(userId);
                                    userWin.Show();
                                    this.Close();
                                }
                                else if (roleId == 2) // Техник
                                {
                                    TechWindow techWin = new TechWindow(userId);
                                    techWin.Show();
                                    this.Close();
                                }
                                else if (roleId == 3) // Администратор
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
    }
}