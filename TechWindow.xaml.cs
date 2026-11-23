using MaterialDesignThemes.Wpf;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
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

namespace PrinterCartridgeApp
{
    public partial class TechWindow : Window
    {
        private int currentTechId;

        public TechWindow(int userId)
        {
            InitializeComponent();
            currentTechId = userId;
            LoadRequests();
        }

        // Загрузка заявок (делим на активные и выполненные)
        private void LoadRequests()
        {
            using (SqlConnection connection = new SqlConnection(DbHelper.ConnectionString))
            {
                connection.Open();
                // Собираем названия картриджей в одну строку через STRING_AGG
                string baseQuery = @"
                    SELECT r.RequestID, u.FullName as UserName, r.RoomNumber, 
                           r.CreatedDate, r.Status, r.Comment,
                           STRING_AGG(c.Name, ', ') as CartridgeNames
                    FROM Requests r
                    JOIN Users u ON r.UserID = u.UserID
                    JOIN RequestDetails rd ON r.RequestID = rd.RequestID
                    JOIN CartridgeTypes c ON rd.CartridgeTypeID = c.CartridgeTypeID
                    WHERE {0}
                    GROUP BY r.RequestID, u.FullName, r.RoomNumber, r.CreatedDate, r.Status, r.Comment
                    ORDER BY r.CreatedDate DESC";

                // Активные
                string activeQuery = string.Format(baseQuery, "r.Status IN (N'Новая', N'В работе')");
                SqlDataAdapter adapterActive = new SqlDataAdapter(activeQuery, connection);
                DataTable dtActive = new DataTable();
                adapterActive.Fill(dtActive);
                dgActiveRequests.ItemsSource = dtActive.DefaultView;

                // Выполненные
                string completedQuery = string.Format(baseQuery, "r.Status = N'Выполнена'");
                SqlDataAdapter adapterCompleted = new SqlDataAdapter(completedQuery, connection);
                DataTable dtCompleted = new DataTable();
                adapterCompleted.Fill(dtCompleted);
                dgCompletedRequests.ItemsSource = dtCompleted.DefaultView;
            }
        }

        // Взять заявку в работу
        private void btnTakeWork_Click(object sender, RoutedEventArgs e)
        {
            if (dgActiveRequests.SelectedItem == null)
            {
                MessageBox.Show("Выберите заявку из вкладки 'АКТИВНЫЕ'!", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DataRowView row = (DataRowView)dgActiveRequests.SelectedItem;
            string currentStatus = row["Status"].ToString();
            int requestId = Convert.ToInt32(row["RequestID"]);

            if (currentStatus == "В работе")
            {
                MessageBox.Show("Эта заявка уже взята в работу!", "Внимание", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            using (SqlConnection connection = new SqlConnection(DbHelper.ConnectionString))
            {
                connection.Open();
                string query = "UPDATE Requests SET Status = N'В работе', TechnicianID = @techId WHERE RequestID = @id";
                using (SqlCommand cmd = new SqlCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@techId", currentTechId);
                    cmd.Parameters.AddWithValue("@id", requestId);
                    cmd.ExecuteNonQuery();
                }
            }
            LoadRequests();
        }

        // Выполнить заявку (списать все картриджи со склада)
        private void btnComplete_Click(object sender, RoutedEventArgs e)
        {
            if (dgActiveRequests.SelectedItem == null)
            {
                MessageBox.Show("Выберите заявку из вкладки 'АКТИВНЫЕ'!", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DataRowView row = (DataRowView)dgActiveRequests.SelectedItem;
            int requestId = Convert.ToInt32(row["RequestID"]);
            string cartridgeNames = row["CartridgeNames"].ToString();

            using (SqlConnection connection = new SqlConnection(DbHelper.ConnectionString))
            {
                connection.Open();

                // 1. Получаем список всех ID картриджей в этой заявке
                List<int> cartridgeIds = new List<int>();
                string getDetailsQuery = "SELECT CartridgeTypeID FROM RequestDetails WHERE RequestID = @id";
                using (SqlCommand cmd = new SqlCommand(getDetailsQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@id", requestId);
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            cartridgeIds.Add(Convert.ToInt32(reader["CartridgeTypeID"]));
                        }
                    }
                }

                // 2. Проверяем наличие каждого картриджа на складе
                foreach (int cartId in cartridgeIds)
                {
                    string checkStockQuery = "SELECT Quantity FROM Inventory WHERE CartridgeTypeID = @cartId";
                    int stock = 0;
                    using (SqlCommand cmd = new SqlCommand(checkStockQuery, connection))
                    {
                        cmd.Parameters.AddWithValue("@cartId", cartId);
                        stock = (int)cmd.ExecuteScalar();
                    }

                    if (stock <= 0)
                    {
                        MessageBox.Show($"На складе нет одного из картриджей ({cartridgeNames})! Пополните склад.", "Ошибка склада", MessageBoxButton.OK, MessageBoxImage.Error);
                        return; // Прерываем, если чего-то не хватает
                    }
                }

                // 3. Используем транзакцию для списания и закрытия
                using (SqlTransaction transaction = connection.BeginTransaction())
                {
                    try
                    {
                        // Списываем каждый картридж
                        foreach (int cartId in cartridgeIds)
                        {
                            string updateStockQuery = "UPDATE Inventory SET Quantity = Quantity - 1 WHERE CartridgeTypeID = @cartId";
                            using (SqlCommand cmd = new SqlCommand(updateStockQuery, connection, transaction))
                            {
                                cmd.Parameters.AddWithValue("@cartId", cartId);
                                cmd.ExecuteNonQuery();
                            }
                        }

                        // Закрываем заявку
                        string updateRequestQuery = @"
                            UPDATE Requests 
                            SET Status = N'Выполнена', 
                                TechnicianID = @techId, 
                                CompletionDate = GETDATE(), 
                                Comment = @comment 
                            WHERE RequestID = @id";

                        using (SqlCommand cmd = new SqlCommand(updateRequestQuery, connection, transaction))
                        {
                            cmd.Parameters.AddWithValue("@techId", currentTechId);
                            cmd.Parameters.AddWithValue("@comment", txtComment.Text.Trim());
                            cmd.Parameters.AddWithValue("@id", requestId);
                            cmd.ExecuteNonQuery();
                        }

                        transaction.Commit();
                        MessageBox.Show($"Заявка выполнена, картриджи ({cartridgeNames}) списаны со склада!", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                        txtComment.Clear();
                        LoadRequests();
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        MessageBox.Show("Ошибка при выполнении заявки: " + ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        // Тема (без изменений)
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