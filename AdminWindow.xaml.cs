using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Data;
using System.Data.SqlClient;
using ClosedXML.Excel;
using Microsoft.Win32;
using MaterialDesignThemes.Wpf;

namespace PrinterCartridgeApp
{
    public partial class AdminWindow : Window
    {
        private int? editingUserId = null;

        public AdminWindow(int userId)
        {
            InitializeComponent();
            LoadInventory();
            LoadUserStats();
            LoadDashboard();
            LoadUsers();
        }

        // Загрузка склада
        private void LoadInventory()
        {
            using (SqlConnection connection = new SqlConnection(DbHelper.ConnectionString))
            {
                connection.Open();
                string query = @"
            SELECT i.InventoryID, c.Name, c.Description, i.Quantity,
                   CASE WHEN i.Quantity <= 3 THEN 1 ELSE 0 END as IsLowStock,
                   ISNULL(consumed.Cnt, 0) as ConsumedLast4Weeks,
                   CASE 
                       WHEN ISNULL(consumed.Cnt, 0) = 0 THEN 999.0
                       ELSE CAST(i.Quantity * 4.0 / consumed.Cnt AS DECIMAL(5,1))
                   END as WeeksLeft
            FROM Inventory i
            JOIN CartridgeTypes c ON i.CartridgeTypeID = c.CartridgeTypeID
            LEFT JOIN (
                SELECT rd.CartridgeTypeID, COUNT(*) as Cnt
                FROM RequestDetails rd
                JOIN Requests r ON rd.RequestID = r.RequestID
                WHERE r.Status = N'Выполнена' AND r.CompletionDate >= DATEADD(day, -28, GETDATE())
                GROUP BY rd.CartridgeTypeID
            ) consumed ON i.CartridgeTypeID = consumed.CartridgeTypeID";

                SqlDataAdapter adapter = new SqlDataAdapter(query, connection);
                DataTable dt = new DataTable();
                adapter.Fill(dt);
                dgInventory.ItemsSource = dt.DefaultView;

                bool hasLowStock = false;
                foreach (DataRow row in dt.Rows)
                {
                    if (Convert.ToInt32(row["IsLowStock"]) == 1) { hasLowStock = true; break; }
                }
                pnlWarning.Visibility = hasLowStock ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        // Загрузка статистики по пользователям
        private void LoadUserStats()
        {
            using (SqlConnection connection = new SqlConnection(DbHelper.ConnectionString))
            {
                connection.Open();
                // Считаем количество заявок для каждого пользователя
                string query = @"
                SELECT u.FullName, 
                COUNT(r.RequestID) as RequestCount,
                CASE WHEN COUNT(r.RequestID) >= 3 THEN 1 ELSE 0 END as IsSuspicious
                FROM Users u
                LEFT JOIN Requests r ON u.UserID = r.UserID 
                AND r.CreatedDate >= DATEADD(day, -7, GETDATE())
                WHERE u.RoleID = 1
                GROUP BY u.FullName
                ORDER BY RequestCount DESC";

                SqlDataAdapter adapter = new SqlDataAdapter(query, connection);
                DataTable dt = new DataTable();
                adapter.Fill(dt);
                dgUserStats.ItemsSource = dt.DefaultView;
            }
        }

        // Добавить картридж на склад
        private void btnAddStock_Click(object sender, RoutedEventArgs e)
        {
            if (dgInventory.SelectedItem == null)
            {
                MessageBox.Show("Выберите картридж в таблице!", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DataRowView row = (DataRowView)dgInventory.SelectedItem;
            int inventoryId = Convert.ToInt32(row["InventoryID"]);

            using (SqlConnection connection = new SqlConnection(DbHelper.ConnectionString))
            {
                connection.Open();
                string query = "UPDATE Inventory SET Quantity = Quantity + 1 WHERE InventoryID = @id";
                using (SqlCommand cmd = new SqlCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@id", inventoryId);
                    cmd.ExecuteNonQuery();
                }
            }
            LoadInventory(); // Обновить таблицу
        }

        // Списать картридж со склада
        private void btnRemoveStock_Click(object sender, RoutedEventArgs e)
        {
            if (dgInventory.SelectedItem == null)
            {
                MessageBox.Show("Выберите картридж в таблице!", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DataRowView row = (DataRowView)dgInventory.SelectedItem;
            int inventoryId = Convert.ToInt32(row["InventoryID"]);
            int currentQty = Convert.ToInt32(row["Quantity"]);

            if (currentQty <= 0)
            {
                MessageBox.Show("На складе уже 0 штук, списывать нечего!", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            using (SqlConnection connection = new SqlConnection(DbHelper.ConnectionString))
            {
                connection.Open();
                string query = "UPDATE Inventory SET Quantity = Quantity - 1 WHERE InventoryID = @id";
                using (SqlCommand cmd = new SqlCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@id", inventoryId);
                    cmd.ExecuteNonQuery();
                }
            }
            LoadInventory(); // Обновить таблицу
        }

        // Формирование отчета за 7 дней
        private void btnGenerateReport_Click(object sender, RoutedEventArgs e)
        {
            using (SqlConnection connection = new SqlConnection(DbHelper.ConnectionString))
            {
                connection.Open();
                // Группируем по пользователю и картриджу
                string query = @"
            SELECT u.FullName as UserName, c.Name as CartridgeName, COUNT(rd.DetailID) as UsedCount
            FROM Requests r
            JOIN Users u ON r.UserID = u.UserID
            JOIN RequestDetails rd ON r.RequestID = rd.RequestID
            JOIN CartridgeTypes c ON rd.CartridgeTypeID = c.CartridgeTypeID
            WHERE r.Status = N'Выполнена' 
              AND r.CompletionDate >= DATEADD(day, -7, GETDATE())
            GROUP BY u.FullName, c.Name
            ORDER BY u.FullName, c.Name";

                SqlDataAdapter adapter = new SqlDataAdapter(query, connection);
                DataTable dt = new DataTable();
                adapter.Fill(dt);

                if (dt.Rows.Count == 0)
                {
                    MessageBox.Show("За последние 7 дней не было выполненных заявок.", "Отчет пуст", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                dgReport.ItemsSource = dt.DefaultView;
            }
        }
        private void btnExportExcel_Click(object sender, RoutedEventArgs e)
        {
            if (dgReport.ItemsSource == null || dgReport.Items.Count == 0)
            {
                MessageBox.Show("Сначала сформируйте отчет!", "Нет данных", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                Filter = "Excel File|*.xlsx",
                Title = "Сохранить отчет по расходу",
                FileName = "Отчет_Расход_" + DateTime.Now.ToString("yyyy-MM-dd")
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    using (var workbook = new XLWorkbook())
                    {
                        var worksheet = workbook.Worksheets.Add("Расход за 7 дней");

                        // Новые заголовки
                        worksheet.Cell(1, 1).Value = "Сотрудник";
                        worksheet.Cell(1, 2).Value = "Картридж";
                        worksheet.Cell(1, 3).Value = "Израсходовано (шт)";

                        worksheet.Range("A1:C1").Style.Font.Bold = true;

                        int row = 2;
                        foreach (DataRowView item in dgReport.Items)
                        {
                            worksheet.Cell(row, 1).Value = item["UserName"].ToString();
                            worksheet.Cell(row, 2).Value = item["CartridgeName"].ToString();
                            worksheet.Cell(row, 3).Value = Convert.ToInt32(item["UsedCount"]);
                            row++;
                        }

                        worksheet.Columns().AdjustToContents();
                        workbook.SaveAs(saveFileDialog.FileName);
                    }
                    MessageBox.Show("Отчет успешно сохранен в Excel!", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Ошибка при сохранении файла: " + ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        // Включаем темную тему
        private void ThemeToggle_Checked(object sender, RoutedEventArgs e)
        {
            var paletteHelper = new PaletteHelper();
            var theme = paletteHelper.GetTheme();

            theme.SetBaseTheme(BaseTheme.Dark); // Устанавливаем темную основу
            paletteHelper.SetTheme(theme);

            ThemeIcon.Kind = PackIconKind.WeatherSunny; // Меняем иконку на солнышко
        }

        // Возвращаем светлую тему
        private void ThemeToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            var paletteHelper = new PaletteHelper();
            var theme = paletteHelper.GetTheme();

            theme.SetBaseTheme(BaseTheme.Light); // Устанавливаем светлую основу
            paletteHelper.SetTheme(theme);

            ThemeIcon.Kind = PackIconKind.WeatherNight; // Меняем иконку на луну
        }
        // Загрузка данных для панели приборов
        private void LoadDashboard()
        {
            using (SqlConnection connection = new SqlConnection(DbHelper.ConnectionString))
            {
                connection.Open();

                // 1. Всего заявок
                string queryTotal = "SELECT COUNT(*) FROM Requests";
                using (SqlCommand cmd = new SqlCommand(queryTotal, connection))
                {
                    txtTotalRequests.Text = cmd.ExecuteScalar().ToString();
                }

                // 2. Активные заявки (Новые + В работе)
                string queryPending = "SELECT COUNT(*) FROM Requests WHERE Status IN (N'Новая', N'В работе')";
                using (SqlCommand cmd = new SqlCommand(queryPending, connection))
                {
                    txtPendingRequests.Text = cmd.ExecuteScalar().ToString();
                }

                // 3. Выполненные заявки
                string queryCompleted = "SELECT COUNT(*) FROM Requests WHERE Status = N'Выполнена'";
                using (SqlCommand cmd = new SqlCommand(queryCompleted, connection))
                {
                    txtCompletedRequests.Text = cmd.ExecuteScalar().ToString();
                }

                // 4. Общее количество картриджей на складе
                string queryStock = "SELECT ISNULL(SUM(Quantity), 0) FROM Inventory";
                using (SqlCommand cmd = new SqlCommand(queryStock, connection))
                {
                    txtTotalStock.Text = cmd.ExecuteScalar().ToString();
                }
            }
        }
        private void LoadUsers()
        {
            using (SqlConnection connection = new SqlConnection(DbHelper.ConnectionString))
            {
                connection.Open();
                string query = @"
            SELECT u.UserID, u.FullName, u.Email, u.Login, u.RoleID, r.RoleName, u.IsActive,
                   CASE WHEN u.IsActive = 1 THEN N'Активен' ELSE N'Заблокирован' END as StatusText
            FROM Users u
            JOIN Roles r ON u.RoleID = r.RoleID
            ORDER BY u.UserID";

                SqlDataAdapter adapter = new SqlDataAdapter(query, connection);
                DataTable dt = new DataTable();
                adapter.Fill(dt);
                dgUsers.ItemsSource = dt.DefaultView;
            }
        }

        private void btnSaveUser_Click(object sender, RoutedEventArgs e)
        {
            string fullName = txtUserFullName.Text.Trim();
            string email = txtUserEmail.Text.Trim();
            string login = txtUserLogin.Text.Trim();
            string password = txtUserPassword.Password.Trim();

            if (string.IsNullOrEmpty(fullName) || string.IsNullOrEmpty(login) || cbUserRole.SelectedItem == null)
            {
                MessageBox.Show("Заполните ФИО, Логин и выберите роль!", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int roleId = Convert.ToInt32(((ComboBoxItem)cbUserRole.SelectedItem).Tag);

            // Проверка пароля (только если он введен)
            if (!string.IsNullOrEmpty(password) && password.Length < 8)
            {
                MessageBox.Show("Пароль должен содержать минимум 8 символов!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            using (SqlConnection connection = new SqlConnection(DbHelper.ConnectionString))
            {
                try
                {
                    connection.Open();
                    if (editingUserId == null) // ДОБАВЛЕНИЕ
                    {
                        if (string.IsNullOrEmpty(password))
                        {
                            MessageBox.Show("Для нового пользователя нужно задать пароль!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }

                        string hashedPassword = DbHelper.HashPassword(password);
                        string query = "INSERT INTO Users (FullName, Email, Login, Password, RoleID, IsActive) VALUES (@fn, @em, @log, @pass, @role, 1)";
                        using (SqlCommand cmd = new SqlCommand(query, connection))
                        {
                            cmd.Parameters.AddWithValue("@fn", fullName);
                            cmd.Parameters.AddWithValue("@em", email);
                            cmd.Parameters.AddWithValue("@log", login);
                            cmd.Parameters.AddWithValue("@pass", hashedPassword);
                            cmd.Parameters.AddWithValue("@role", roleId);
                            cmd.ExecuteNonQuery();
                        }
                        MessageBox.Show("Пользователь успешно добавлен!", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else // РЕДАКТИРОВАНИЕ
                    {
                        string query;
                        using (SqlCommand cmd = new SqlCommand())
                        {
                            cmd.Connection = connection;
                            if (string.IsNullOrEmpty(password)) // Если пароль не меняем
                            {
                                query = "UPDATE Users SET FullName = @fn, Email = @em, Login = @log, RoleID = @role WHERE UserID = @id";
                            }
                            else // Если меняем пароль
                            {
                                query = "UPDATE Users SET FullName = @fn, Email = @em, Login = @log, Password = @pass, RoleID = @role WHERE UserID = @id";
                                cmd.Parameters.AddWithValue("@pass", DbHelper.HashPassword(password));
                            }

                            cmd.CommandText = query;
                            cmd.Parameters.AddWithValue("@fn", fullName);
                            cmd.Parameters.AddWithValue("@em", email);
                            cmd.Parameters.AddWithValue("@log", login);
                            cmd.Parameters.AddWithValue("@role", roleId);
                            cmd.Parameters.AddWithValue("@id", editingUserId);
                            cmd.ExecuteNonQuery();
                        }
                        MessageBox.Show("Данные пользователя обновлены!", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                    }

                    ResetUserForm();
                    LoadUsers();
                }
                catch (SqlException ex)
                {
                    if (ex.Number == 2627) // Ошибка уникальности логина
                        MessageBox.Show("Пользователь с таким логином уже существует!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    else
                        MessageBox.Show("Ошибка БД: " + ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void btnEditUser_Click(object sender, RoutedEventArgs e)
        {
            if (dgUsers.SelectedItem == null)
            {
                MessageBox.Show("Выберите пользователя из таблицы!", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DataRowView row = (DataRowView)dgUsers.SelectedItem;
            editingUserId = Convert.ToInt32(row["UserID"]);

            txtUserFormTitle.Text = "Редактирование пользователя";
            txtUserFullName.Text = row["FullName"].ToString();
            txtUserEmail.Text = row["Email"].ToString();
            txtUserLogin.Text = row["Login"].ToString();
            txtUserPassword.Clear(); // Пароль не показываем

            // Выбираем роль в ComboBox
            int roleId = Convert.ToInt32(row["RoleID"]);
            foreach (ComboBoxItem item in cbUserRole.Items)
            {
                if (Convert.ToInt32(item.Tag) == roleId) { cbUserRole.SelectedItem = item; break; }
            }

            btnSaveUser.Content = "СОХРАНИТЬ ИЗМЕНЕНИЯ";
            btnCancelUser.Visibility = Visibility.Visible;
        }

        private void btnToggleBlock_Click(object sender, RoutedEventArgs e)
        {
            if (dgUsers.SelectedItem == null)
            {
                MessageBox.Show("Выберите пользователя!", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DataRowView row = (DataRowView)dgUsers.SelectedItem;
            int userId = Convert.ToInt32(row["UserID"]);
            bool isActive = Convert.ToBoolean(row["IsActive"]);
            string fullName = row["FullName"].ToString();

            if (userId == 1) // Защита главного админа (если его ID = 1)
            {
                MessageBox.Show("Нельзя заблокировать главного администратора!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Stop);
                return;
            }

            string action = isActive ? "заблокировать" : "разблокировать";
            if (MessageBox.Show($"Вы уверены, что хотите {action} пользователя {fullName}?", "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                using (SqlConnection connection = new SqlConnection(DbHelper.ConnectionString))
                {
                    connection.Open();
                    string query = "UPDATE Users SET IsActive = CASE WHEN IsActive = 1 THEN 0 ELSE 1 END WHERE UserID = @id";
                    using (SqlCommand cmd = new SqlCommand(query, connection))
                    {
                        cmd.Parameters.AddWithValue("@id", userId);
                        cmd.ExecuteNonQuery();
                    }
                }
                LoadUsers();
            }
        }

        private void btnCancelUser_Click(object sender, RoutedEventArgs e)
        {
            ResetUserForm();
        }

        private void ResetUserForm()
        {
            editingUserId = null;
            txtUserFormTitle.Text = "Добавление пользователя";
            txtUserFullName.Clear();
            txtUserEmail.Clear();
            txtUserLogin.Clear();
            txtUserPassword.Clear();
            cbUserRole.SelectedIndex = -1;
            btnSaveUser.Content = "СОХРАНИТЬ";
            btnCancelUser.Visibility = Visibility.Collapsed;
        }
    }
}