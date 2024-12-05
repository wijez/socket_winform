using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows.Forms;

namespace Client
{
    public partial class frm_Client : Form
    {
        private const string CLOSE_COMMAND = "0";
        private const string SEND_MESSAGE = "1";
        private const string SEND_IMAGE = "2";
        private const string SEND_FILE = "3";
        private const string SEND_EMOJI = "4";

        private Socket client;
        private List<Socket> clients = new List<Socket>();
        private IPEndPoint iPEndPoint;
        private const int PORT = 5555;
        private byte[] buffer;
        private Image prevEmoji = null;
        public frm_Client()
        {
            InitializeComponent();

            CheckForIllegalCrossThreadCalls = false;
            client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            iPEndPoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), PORT);
        }

        private void frm_Client_Load(object sender, EventArgs e)
        {
            startClient();
            pnlMain.Controls.Add(createChatPanelView());
            loadEmoji();
        }

        private void loadEmoji()
        {
            string[] files = Directory.GetFiles("Emojis");
            foreach (string file in files)
            {
                PictureBox picture = new PictureBox();
                picture.Size = new Size(28, 28);
                picture.Image = Image.FromFile(file);
                picture.SizeMode = PictureBoxSizeMode.StretchImage;
                picture.Padding = new Padding(2);
                pnlEmoji.Controls.Add(picture);

                picture.Click += ptbEmojiItem_Click;
            }
        }

        private void ptbEmojiItem_Click(object sender, EventArgs e)
        {
            prevEmoji = (sender as PictureBox).Image;
            pnlEmoji.Visible = false;
            try
            {
                byte[] dataType = Encoding.UTF8.GetBytes(SEND_EMOJI);

                MemoryStream ms = new MemoryStream();
                prevEmoji.Save(ms, ImageFormat.Png);

                byte[] imageData = ms.ToArray();

                byte[] mergedArray = dataType.Concat(imageData).ToArray();


                client.Send(mergedArray);

                Label labelTime = createChatTime();
                Panel panelEmoji = createChatEmoji(prevEmoji, DockStyle.Right);
                panelEmoji.Tag = labelTime;

                pnlMain.Controls[0].Controls.Add(labelTime);
                labelTime.BringToFront();
                pnlMain.Controls[0].Controls.Add(panelEmoji);
                panelEmoji.BringToFront();
                (pnlMain.Controls[0] as Panel).ScrollControlIntoView(panelEmoji);

                ms.Close();
            }
            catch (Exception)
            {
                MessageBox.Show("Lỗi gửi hình ảnh");
            }
        }

        private void startClient()
        {
            client.BeginConnect(iPEndPoint, connectCallback, null);
        }

        private void connectCallback(IAsyncResult ar)
        {
            try
            {
                client.EndConnect(ar);
                BeginInvoke((Action)(() =>
                {
                    CheckBox checkBox = createChatClient(iPEndPoint.ToString() + " (Server)");
                    checkBox.Checked = true;
                    pnlSidebar.Controls.Add(checkBox);

                    lblUsername.Text = checkBox.Text;

                    buffer = new byte[1024 * 20];
                    client.BeginReceive(buffer, 0, buffer.Length, SocketFlags.None, receiveCallback, null);
                }));
            }
            catch (Exception)
            {
                MessageBox.Show("Lỗi kết nối đến máy chủ");
            }
        }

       
        private void receiveCallback(IAsyncResult ar)
        {
            try
            {
                int bytesRead = client.EndReceive(ar);
                if (bytesRead > 0)
                {
                    byte[] receivedData = new byte[bytesRead];
                    Array.Copy(buffer, receivedData, bytesRead);

                    string dataType = Encoding.UTF8.GetString(receivedData, 0, 1);


                    switch (dataType)
                    {
                        case SEND_MESSAGE:
                            string message = Encoding.UTF8.GetString(receivedData, 1, bytesRead - 1);
                            BeginInvoke((Action)(() =>
                            {
                                try
                                {
                                    Label labelTime = createChatTime();
                                    Panel panelMessage = createChatMessage(message, DockStyle.Left);
                                    panelMessage.Tag = labelTime;

                                    pnlMain.Controls[0].Controls.Add(labelTime);
                                    labelTime.BringToFront();
                                    pnlMain.Controls[0].Controls.Add(panelMessage);
                                    panelMessage.BringToFront();
                                    (pnlMain.Controls[0] as Panel).ScrollControlIntoView(panelMessage);
                                }
                                catch (Exception ex)
                                {
                                    MessageBox.Show("Error handling message: " + ex.Message);
                                }
                            }));
                            break;
                        case SEND_IMAGE:
                            BeginInvoke((Action)(() =>
                            {
                                try
                                {
                                    //ImageConverter convertData = new ImageConverter();
                                    //Image image = null;
                                    //using (MemoryStream ms = new MemoryStream(receivedData.Skip(1).ToArray()))
                                    //{
                                    //    image = Image.FromStream(ms);
                                    //}
                                    byte[] data = receivedData.Skip(1).ToArray();

                                    // Kiểm tra tệp là hình ảnh hay PDF
                                    bool isImage = false;
                                    try
                                    {
                                        using (MemoryStream ms = new MemoryStream(data))
                                        {
                                            Image testImage = Image.FromStream(ms);
                                            isImage = true;
                                        }
                                    }
                                    catch
                                    {
                                        // Không làm gì, sẽ xử lý tệp không phải là hình ảnh
                                    }

                                    if (isImage)
                                    {
                                        // Xử lý khi là hình ảnh
                                        using (MemoryStream ms = new MemoryStream(data))
                                        {
                                            Image image = Image.FromStream(ms);

                                            Label labelTime = createChatTime();
                                            Panel panelImage = createChatImage(image, DockStyle.Left);
                                            panelImage.Tag = labelTime;

                                            pnlMain.Controls[0].Controls.Add(labelTime);
                                            labelTime.BringToFront();
                                            pnlMain.Controls[0].Controls.Add(panelImage);
                                            panelImage.BringToFront();
                                            (pnlMain.Controls[0] as Panel).ScrollControlIntoView(panelImage);
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    MessageBox.Show("Error handling image: " + ex.Message);
                                }
                            }));    
                            break;
                        case SEND_FILE:
                            BeginInvoke((Action)(() =>
                            {
                                try
                                {
                                    string fileName = Encoding.UTF8.GetString(receivedData, 1, 256).Trim();
                                    byte[] fileData = receivedData.Skip(257).ToArray();

                                    //string filePath = Path.Combine("ReceivedFiles", fileName);
                                    //Directory.CreateDirectory(Path.GetDirectoryName(filePath)); // Ensure the directory exists
                                    //File.WriteAllBytes(filePath, fileData);

                                    string uniqueFileName = Guid.NewGuid().ToString() + "_" + fileName;
                                    string filePath = Path.Combine("ReceivedFiles", uniqueFileName);

                                    string directory = Path.GetDirectoryName(filePath);
                                    if (!Directory.Exists(directory))
                                    {
                                        Directory.CreateDirectory(directory);
                                    }

                                    using (FileStream fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
                                    {
                                        fs.Write(fileData, 0, fileData.Length);
                                    }

                                    Label labelTime = createChatTime();
                                    Panel panelFile = createChatFile(fileName, DockStyle.Left);
                                    panelFile.Tag = fileName;

                                    pnlMain.Controls[0].Controls.Add(labelTime);
                                    labelTime.BringToFront();
                                    pnlMain.Controls[0].Controls.Add(panelFile);
                                    panelFile.BringToFront();
                                    (pnlMain.Controls[0] as Panel).ScrollControlIntoView(panelFile);
                                }
                                catch (Exception ex)
                                {
                                    MessageBox.Show("Error handling file: " + ex.Message);
                                }
                            }));
                            break;
                        case SEND_EMOJI:
                            BeginInvoke((Action)(() =>
                            {
                                ImageConverter convertData = new ImageConverter();
                                Image image = null;
                                using (MemoryStream ms = new MemoryStream(receivedData.Skip(1).ToArray()))
                                {
                                    image = Image.FromStream(ms);
                                }

                                Label labelTime = createChatTime();
                                Panel panelEmoji = createChatEmoji(image, DockStyle.Left);
                                panelEmoji.Tag = labelTime;

                                pnlMain.Controls[0].Controls.Add(labelTime);
                                labelTime.BringToFront();
                                pnlMain.Controls[0].Controls.Add(panelEmoji);
                                panelEmoji.BringToFront();
                                (pnlMain.Controls[0] as Panel).ScrollControlIntoView(panelEmoji);

                            }));
                            break;
                    }

                    buffer = new byte[1024 * 100];
                    client.BeginReceive(buffer, 0, buffer.Length, SocketFlags.None, receiveCallback, null);
                }
                else
                {
                    client.Close();
                }
            }
            catch (Exception)
            {
                MessageBox.Show("Lỗi nhận dữ liệu từ máy chủ");
            }
        }


        private void sendMessage(string message)
        {
            try
            {
                byte[] data = Encoding.UTF8.GetBytes(message);
                client.BeginSend(data, 0, data.Length, SocketFlags.None, sendCallback, null);
            }
            catch (Exception)
            {
                MessageBox.Show("Lỗi gửi dữ liệu đến máy chủ");
            }
        }

        private void sendCallback(IAsyncResult ar)
        {
            try
            {
                client.EndSend(ar);
                BeginInvoke((Action)(() =>
                {

                    Label labelTime = createChatTime();
                    Panel panelMessage = createChatMessage(txtMessage.Text, DockStyle.Right);
                    panelMessage.Tag = labelTime;

                    pnlMain.Controls[0].Controls.Add(labelTime);
                    labelTime.BringToFront();
                    pnlMain.Controls[0].Controls.Add(panelMessage);
                    panelMessage.BringToFront();
                    (pnlMain.Controls[0] as Panel).ScrollControlIntoView(panelMessage);

                    txtMessage.Clear();
                    txtMessage.Focus();
                }));
            }
            catch (Exception)
            {
                MessageBox.Show("Lỗi kết thúc gửi dữ liệu máy chủ");
            }
        }

        private string getCurrentDateTime()
        {
            return DateTime.Now.ToString("HH:mm:ss dd/MM/yyyy");
        }

        private Label createChatTime()
        {
            Label label = new Label();
            label.Text = getCurrentDateTime();
            label.AutoSize = false;
            label.Dock = DockStyle.Top;
            label.TextAlign = ContentAlignment.MiddleCenter;
            label.Font = new Font(label.Font.FontFamily, 9);
            label.Size = new Size(0, 45);
            label.ForeColor = Color.FromArgb(140, 140, 140);

            return label;
        }

        private Panel createChatPanelView()
        {
            Panel panel = new Panel();
            panel.Dock = DockStyle.Fill;
            panel.ForeColor = Color.Black;
            panel.Padding = new Padding(20, 10, 20, 20);
            panel.BackColor = Color.White;
            panel.AutoScroll = true;
            return panel;
        }

        private CheckBox createChatClient(string chatName)
        {
            CheckBox checkBox = new CheckBox();
            checkBox.Text = chatName;
            checkBox.AutoSize = false;
            checkBox.Dock = DockStyle.Top;
            checkBox.Size = new Size(0, 50);
            checkBox.ForeColor = Color.Black;
            checkBox.Padding = new Padding(10);
            checkBox.TextAlign = ContentAlignment.MiddleLeft;
            checkBox.CheckedChanged += chatClient_CheckedChanged;
            checkBox.Click += chatClient_Click;

            return checkBox;
        }

        private void chatClient_CheckedChanged(object sender, EventArgs e)
        {
            CheckBox chatLabel = sender as CheckBox;

            if (chatLabel.Checked)
            {
                chatLabel.BackColor = Color.FromArgb(229, 239, 255);
            }
            else
            {
                chatLabel.BackColor = Color.White;
            }

        }

        private void chatClient_Click(object sender, EventArgs e)
        {
            CheckBox chatLabel = sender as CheckBox;

            lblUsername.Text = chatLabel.Text;

            int index = pnlSidebar.Controls.GetChildIndex(chatLabel);
          
        }

        private Panel createChatMessage(string message, DockStyle dock)
        {
            Panel panel = new Panel();
            panel.Dock = DockStyle.Top;

            Label label = new Label();
            label.Text = message;
            label.Dock = dock;
            label.MaximumSize = new Size(300, 500);
            label.AutoSize = true;
            label.BorderStyle = BorderStyle.FixedSingle;
            label.Font = new Font(label.Font.FontFamily, 12);
            label.ForeColor = dock == DockStyle.Right ? Color.Gray : Color.Black;
            label.Padding = new Padding(10);
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.BackColor = dock == DockStyle.Right ? Color.FromArgb(245, 228, 145) : Color.FromArgb(3, 221, 17);
            label.AutoEllipsis = true;

            PictureBox ptbDelete = new PictureBox();
            ptbDelete.Size = new Size(18, 18);
            ptbDelete.Image = Properties.Resources.icons8_delete_50;
            ptbDelete.SizeMode = PictureBoxSizeMode.StretchImage;
            ptbDelete.Dock = DockStyle.Bottom;

            PictureBox ptbForward = new PictureBox();
            ptbForward.Size = new Size(18, 18);
            ptbForward.Image = Properties.Resources.icons8_forward_50 ; 
            ptbForward.SizeMode = PictureBoxSizeMode.StretchImage;
            ptbForward.Dock = DockStyle.Bottom;

            Panel pnlDelete = new Panel();
            pnlDelete.Dock = dock;
            pnlDelete.Controls.Add(ptbDelete);
            pnlDelete.Controls.Add(ptbForward);
            pnlDelete.Size = new Size(28, pnlDelete.Height);
            pnlDelete.Padding = new Padding(5, 5, 5, 10);

            panel.Controls.Add(label);
            panel.Height = label.Height;
            panel.Controls.Add(pnlDelete);
            pnlDelete.BringToFront();

            ptbDelete.Click += ptbDelete_Click;
            ptbForward.Click += (sender, e) => ForwardMessage(message);

            return panel;
        }

        private void ForwardMessage(string message)
        {
            Form forwardForm = new Form();
            forwardForm.Text = "Chuyển tiếp tin nhắn";
            forwardForm.Size = new Size(300, 200);

            Label lblSelectClient = new Label();
            lblSelectClient.Text = "Chọn Client:";
            lblSelectClient.Dock = DockStyle.Top;
            forwardForm.Controls.Add(lblSelectClient);

            ComboBox cmbClients = new ComboBox();
            cmbClients.Dock = DockStyle.Top;
            foreach (Control control in pnlSidebar.Controls)
            {
                if (control is CheckBox checkBox)
                {
                    cmbClients.Items.Add(checkBox.Text);
                }
            }
            forwardForm.Controls.Add(cmbClients);

            Button btnSend = new Button();
            btnSend.Text = "Send";
            btnSend.Dock = DockStyle.Top;
            btnSend.Click += (sender, e) =>
            {
                if (cmbClients.SelectedIndex != -1)
                {
                    string selectedClient = cmbClients.SelectedItem.ToString();
                    sendMessage(SEND_MESSAGE + selectedClient + ":" + message);
                    forwardForm.Close();
                }
                else
                {
                    MessageBox.Show("Please select a client to forward the message.");
                }
            };
            forwardForm.Controls.Add(btnSend);

            forwardForm.ShowDialog();
        }

        private Panel createChatImage(Image image, DockStyle dock)
        {
            Panel panel = new Panel();
            panel.Dock = DockStyle.Top;

            PictureBox picture = new PictureBox();
            picture.AutoSize = false;
            picture.Dock = dock;
            picture.Size = calculateAspectRatio(image.Size, 300);
            picture.SizeMode = PictureBoxSizeMode.StretchImage;
            picture.Image = image;
            picture.MaximumSize = new Size(300, 700);
            picture.BackColor = Color.White;

            PictureBox ptbDelete = new PictureBox();
            ptbDelete.Size = new Size(18, 18);
            ptbDelete.Image = Properties.Resources.icons8_delete_50;
            ptbDelete.SizeMode = PictureBoxSizeMode.StretchImage;
            ptbDelete.Dock = DockStyle.Bottom;


            Panel pnlDelete = new Panel();
            pnlDelete.Dock = dock;
            pnlDelete.Controls.Add(ptbDelete);
            pnlDelete.Size = new Size(28, pnlDelete.Height);
            pnlDelete.Padding = new Padding(5, 5, 5, 10);

            panel.Controls.Add(picture);
            panel.Size = calculateAspectRatio(image.Size, 300);
            panel.Controls.Add(pnlDelete);
            pnlDelete.BringToFront();

            ptbDelete.Click += ptbDelete_Click;

            return panel;
        }

       
        private void sendFile(string filePath)
        {
            try
            {
                string fileName = Path.GetFileName(filePath);
                byte[] fileData = File.ReadAllBytes(filePath);

                byte[] dataType = Encoding.UTF8.GetBytes(SEND_FILE);
                byte[] fileNameBytes = Encoding.UTF8.GetBytes(fileName.PadRight(256));
                byte[] mergedArray = dataType.Concat(fileNameBytes).Concat(fileData).ToArray();

                client.Send(mergedArray);



                Label labelTime = createChatTime();
                Panel panelFile = createChatFile(fileName, DockStyle.Right);
                panelFile.Tag = fileName;

                pnlMain.Controls[0].Controls.Add(labelTime);
                labelTime.BringToFront();
                pnlMain.Controls[0].Controls.Add(panelFile);
                panelFile.BringToFront();
                (pnlMain.Controls[0] as Panel).ScrollControlIntoView(panelFile);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error sending file '{Path.GetFileName(filePath)}': {ex.Message}");
            }
        }

        private Panel createChatFile(string fileName, DockStyle dock)
        {
            Panel panel = new Panel();
            panel.Dock = DockStyle.Top;
            panel.Tag = fileName;

            Label label = new Label();
            label.Text = fileName;
            label.Dock = dock;
            label.MaximumSize = new Size(300, 500);
            label.AutoSize = true;
            label.BorderStyle = BorderStyle.FixedSingle;
            label.Font = new Font(label.Font.FontFamily, 12);
            label.ForeColor = dock == DockStyle.Right ? Color.Gray : Color.Black;
            label.Padding = new Padding(10);
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.BackColor = dock == DockStyle.Right ? Color.FromArgb(245, 228, 145) : Color.FromArgb(3, 221, 17);
            label.AutoEllipsis = true;

            label.Click += label_Click;

            PictureBox ptbDelete = new PictureBox();
            ptbDelete.Size = new Size(18, 18);
            ptbDelete.Image = Properties.Resources.icons8_delete_50;
            ptbDelete.SizeMode = PictureBoxSizeMode.StretchImage;
            ptbDelete.Dock = DockStyle.Bottom;

            Panel pnlDelete = new Panel();
            pnlDelete.Dock = dock;
            pnlDelete.Controls.Add(ptbDelete);
            pnlDelete.Size = new Size(28, pnlDelete.Height);
            pnlDelete.Padding = new Padding(5, 5, 5, 10);

            panel.Controls.Add(label);
            panel.Height = label.Height;
            panel.Controls.Add(pnlDelete);
            pnlDelete.BringToFront();

            ptbDelete.Click += ptbDelete_Click;
            panel.Click += panelFile_Click;

            return panel;
        }

        private void label_Click(object sender, EventArgs e)
        {
            Label label = sender as Label;
            string filePath = label?.Tag as string;

            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
            {
                SaveFileDialog saveFileDialog = new SaveFileDialog
                {
                    FileName = Path.GetFileName(filePath),
                    Filter = "All Files|*.*"
                };

                if (saveFileDialog.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        // Sử dụng `FileStream` để đảm bảo tệp không bị khóa
                        using (FileStream sourceStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                        using (FileStream destinationStream = new FileStream(saveFileDialog.FileName, FileMode.Create, FileAccess.Write))
                        {
                            sourceStream.CopyTo(destinationStream);
                        }
                        MessageBox.Show("Tệp đã được tải xuống thành công.", "Thành công", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Đã xảy ra lỗi khi tải tệp: {ex.Message}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
            else
            {
                MessageBox.Show("Tệp không tồn tại.", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void panelFile_Click(object sender, EventArgs e)
        {
            Panel panel = sender as Panel;
            string fileName = panel?.Tag as string;

            if (!string.IsNullOrEmpty(fileName))
            {
                string filePath = Path.Combine("ReceivedFiles", fileName);

                if (File.Exists(filePath))
                {
                    SaveFileDialog saveFileDialog = new SaveFileDialog();
                    saveFileDialog.FileName = fileName;
                    saveFileDialog.Filter = "All Files|*.*";

                    if (saveFileDialog.ShowDialog() == DialogResult.OK)
                    {
                        File.Copy(filePath, saveFileDialog.FileName, true);
                        MessageBox.Show("File downloaded successfully.");
                    }
                }
                else
                {
                    MessageBox.Show("File not found.");
                }
            }
            else
            {
                MessageBox.Show("File name is null or empty.");
            }
        }

        private Panel createChatEmoji(Image image, DockStyle dock)
        {
            Panel panel = new Panel();
            panel.Dock = DockStyle.Top;

            PictureBox picture = new PictureBox();
            picture.AutoSize = false;
            picture.Dock = dock;
            picture.Size = new Size(50, 50);
            picture.SizeMode = PictureBoxSizeMode.StretchImage;
            picture.Image = image;
            picture.BackColor = dock == DockStyle.Right ? Color.FromArgb(0, 145, 255) : Color.FromArgb(229, 239, 255);
            picture.Padding = new Padding(10);

            PictureBox ptbDelete = new PictureBox();
            ptbDelete.Size = new Size(18, 18);
            ptbDelete.Image = Properties.Resources.icons8_delete_50;
            ptbDelete.SizeMode = PictureBoxSizeMode.StretchImage;
            ptbDelete.Dock = DockStyle.Bottom;

            Panel pnlDelete = new Panel();
            pnlDelete.Dock = dock;
            pnlDelete.Controls.Add(ptbDelete);
            pnlDelete.Size = new Size(28, pnlDelete.Height);
            pnlDelete.Padding = new Padding(5, 5, 5, 10);

            panel.Controls.Add(picture);
            panel.Size = new Size(50, 50);
            panel.Controls.Add(pnlDelete);
            pnlDelete.BringToFront();

            ptbDelete.Click += ptbDelete_Click;

            return panel;
        }

        private void ptbDelete_Click(object sender, EventArgs e)
        {
            try
            {
                DialogResult dialogResult = MessageBox.Show(
                    "Bạn có muốn xóa tin nhắn này không?",
                    "Xác nhận xóa",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question
                );

                if (dialogResult == DialogResult.Yes)
                {
                    PictureBox ptbDelete = sender as PictureBox;

                    if (ptbDelete == null || ptbDelete.Parent == null || ptbDelete.Parent.Parent == null)
                    {
                        MessageBox.Show("Lỗi: Không tìm thấy đối tượng cần xóa.", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    Control topParent = ptbDelete.Parent.Parent;
                    object tagValue = topParent.Tag;

                    pnlMain.Controls[0].Controls.Remove(topParent);

                    if (tagValue is Control time && pnlMain.Controls[0].Controls.Contains(time))
                        pnlMain.Controls[0].Controls.Remove(time);

                    if (tagValue is string filePath && File.Exists(filePath))
                    {
                        try
                        {
                            File.Delete(filePath); 
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"Không thể xóa file: {ex.Message}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi: {ex.Message}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }


        public Size calculateAspectRatio(Size originalSize, int targetWidth)
        {
            int targetHeight = (targetWidth * originalSize.Height) / originalSize.Width;

            return new Size(targetWidth, targetHeight);
        }

        private void btnSend_Click(object sender, EventArgs e)
        {
            if (txtMessage.Text.Length == 0)
            {
                MessageBox.Show("Vui lòng nhập nội dung tin nhắn");
                return;
            }

            sendMessage(SEND_MESSAGE + txtMessage.Text);

        }

        private void frm_Client_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                DialogResult result = MessageBox.Show("Bạn có chắc chắn muốn đóng ứng dụng?", "Xác nhận đóng ứng dụng", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                if (result == DialogResult.No)
                {
                    e.Cancel = true;
                }
                else
                {
                    sendMessage(CLOSE_COMMAND);
                    client.Close();
                    e.Cancel = false;
                }
            }
        }

        private void txtMessage_TextChanged(object sender, EventArgs e)
        {
            if (txtMessage.Text.Length > 0)
            {
                lblHint.Visible = false;
            }
            else
            {
                lblHint.Visible = true;
            }
        }

        private void lblHint_Click(object sender, EventArgs e)
        {
            lblHint.Visible = false;
            txtMessage.Visible = true;
            txtMessage.Focus();
        }

        private void ptbImage_Click(object sender, EventArgs e)
        {
            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    byte[] dataType = Encoding.UTF8.GetBytes(SEND_IMAGE);

                    Bitmap bmp = new Bitmap(openFileDialog.FileName);
                    MemoryStream ms = new MemoryStream();

                    ImageFormat format = openFileDialog.FileName.EndsWith(".jpg")
                        || openFileDialog.FileName.EndsWith(".jpeg") ?
                        ImageFormat.Jpeg : ImageFormat.Png;

                    bmp.Save(ms, format);
                    byte[] imageData = ms.ToArray();

                    byte[] mergedArray = dataType.Concat(imageData).ToArray();

                    client.Send(mergedArray);

                    ImageConverter convertData = new ImageConverter();
                    Image image = (Image)convertData.ConvertFrom(imageData);

                    Label labelTime = createChatTime();
                    Panel panelImage = createChatImage(image, DockStyle.Right);
                    panelImage.Tag = labelTime;

                    pnlMain.Controls[0].Controls.Add(labelTime);
                    labelTime.BringToFront();
                    pnlMain.Controls[0].Controls.Add(panelImage);
                    panelImage.BringToFront();
                    (pnlMain.Controls[0] as Panel).ScrollControlIntoView(panelImage);

                    bmp.Dispose();
                    ms.Close();
                }
                catch (Exception)
                {
                    MessageBox.Show("Lỗi gửi hình ảnh hình ảnh phải dạng png jpg, ...");
                }
            }
        }

        private void frm_Client_SizeChanged(object sender, EventArgs e)
        {
            pnlLeft.Visible = Width > 800;
        }

        private void ptbEmoji_Click(object sender, EventArgs e)
        {
            pnlEmoji.Visible = !pnlEmoji.Visible;
        }

        private void ptbFile_Click(object sender, EventArgs e)
        {
            openFileDialog.Filter = "Supported Files|*.txt;*.doc;*.docx;*.pdf|All Files|*.*";
            openFileDialog.Title = "Chọn tệp để gửi";

            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                sendFile(openFileDialog.FileName);
            }
        }

        private void openFileDialog_FileOk(object sender, System.ComponentModel.CancelEventArgs e)
        {

        }
    }
}
