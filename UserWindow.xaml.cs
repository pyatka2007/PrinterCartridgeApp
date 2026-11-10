using System;
using System.Data;
using System.Data.SqlClient;
using System.Windows;

namespace PrinterCartridgeApp
{
    public partial class UserWindow : Window
    {
        private int currentUserId;

        public UserWindow(int userId)
        {
            InitializeComponent();
            currentUserId = userId;
            LoadCartridges();
            LoadRequests();
        }

        // Загружаем типы картриджей в выпадающий список
        private void LoadCartridges()
        {
            using (SqlConnection connection = new SqlConnection(DbHelper.ConnectionString))
            {
                connection.Open();
                string query = "SELECT CartridgeTypeID, Name FROM CartridgeTypes";
                SqlDataAdapter adapter = new SqlDataAdapter(query, connection);
                DataTable dt = new DataTable();
                adapter.Fill(dt);

                cbCartridgeType.ItemsSource = dt.DefaultView;
                cbCartridgeType.DisplayMemberPath = "Name"; // Что видит пользователь
                cbCartridgeType.SelectedValuePath = "CartridgeTypeID"; // Что мы берем в код
            }
        }

        // Загружаем историю заявок текущего пользователя
        private void LoadRequests()
        {
            using (SqlConnection connection = new SqlConnection(DbHelper.ConnectionString))
            {
                connection.Open();
                string query = @"
                    SELECT r.RequestID, c.Name as CartridgeName, r.RoomNumber, r.CreatedDate, r.Status, r.Comment 
                    FROM Requests r
                    JOIN CartridgeTypes c ON r.CartridgeTypeID = c.CartridgeTypeID
                    WHERE r.UserID = @userId
                    ORDER BY r.CreatedDate DESC";

                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@userId", currentUserId);
                    SqlDataAdapter adapter = new SqlDataAdapter(command);
                    DataTable dt = new DataTable();
                    adapter.Fill(dt);
                    dgRequests.ItemsSource = dt.DefaultView;
                }
            }
        }

        // Отправка новой заявки
        private void btnSubmitRequest_Click(object sender, RoutedEventArgs e)
        {
            if (cbCartridgeType.SelectedValue == null || string.IsNullOrWhiteSpace(txtRoom.Text))
            {
                MessageBox.Show("Выберите картридж и укажите кабинет!", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string room = txtRoom.Text.Trim();

            using (SqlConnection connection = new SqlConnection(DbHelper.ConnectionString))
            {
                try
                {
                    connection.Open();

                    // --- БЛОК АНТИ-СПАМА ---
                    // Проверяем, есть ли уже активная заявка в этот кабинет от этого пользователя
                    string checkQuery = @"
                SELECT COUNT(*) FROM Requests 
                WHERE UserID = @userId 
                  AND RoomNumber = @room 
                  AND Status IN (N'Новая', N'В работе')";

                    using (SqlCommand checkCmd = new SqlCommand(checkQuery, connection))
                    {
                        checkCmd.Parameters.AddWithValue("@userId", currentUserId);
                        checkCmd.Parameters.AddWithValue("@room", room);

                        int activeRequests = (int)checkCmd.ExecuteScalar();

                        if (activeRequests > 0)
                        {
                            MessageBox.Show("По вашему кабинету уже есть активная заявка!\nДождитесь, пока техник её выполнит.",
                                            "Анти-спам", MessageBoxButton.OK, MessageBoxImage.Stop);
                            return; // Прерываем выполнение, заявка не создается
                        }
                    }
                    // --- КОНЕЦ БЛОКА АНТИ-СПАМА ---

                    // Если проверка пройдена, создаем заявку
                    string insertQuery = "INSERT INTO Requests (UserID, CartridgeTypeID, RoomNumber, Status) VALUES (@userId, @cartridgeId, @room, N'Новая')";

                    using (SqlCommand insertCmd = new SqlCommand(insertQuery, connection))
                    {
                        insertCmd.Parameters.AddWithValue("@userId", currentUserId);
                        insertCmd.Parameters.AddWithValue("@cartridgeId", (int)cbCartridgeType.SelectedValue);
                        insertCmd.Parameters.AddWithValue("@room", room);

                        insertCmd.ExecuteNonQuery();
                    }

                    MessageBox.Show("Заявка успешно создана!", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                    txtRoom.Clear();
                    cbCartridgeType.SelectedIndex = -1;
                    LoadRequests(); // Обновляем таблицу
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Ошибка при создании заявки: " + ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}