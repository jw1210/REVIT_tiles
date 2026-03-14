using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using TilePlanner.Core;

namespace TilePlanner.UI
{
    /// <summary>
    /// 磁磚計畫設定對話框（純 C# 建構，不需 XAML 編譯）
    /// </summary>
    public class TilePlannerDialog : Window
    {
        // 磁磚設定
        private TextBox txtTileWidth;
        private TextBox txtTileHeight;

        // 灰縫設定
        private TextBox txtHGroutWidth;
        private TextBox txtVGroutWidth;

        // 排列模式
        private RadioButton rbGrid;
        private RadioButton rbRunningBond;
        private StackPanel pnlOffset;
        private TextBox txtOffsetInput;

        // 快速選擇
        private ComboBox cmbPreset;

        public TilePlannerDialog()
        {
            BuildUI();
        }

        public TileConfig GetTileConfig()
        {
            double offsetPercent = ParseDouble(txtOffsetInput.Text, 50);
            // 限制在 1~99 之間
            offsetPercent = Math.Max(1, Math.Min(99, offsetPercent));

            return new TileConfig
            {
                TileWidth = ParseDouble(txtTileWidth.Text, 200),
                TileHeight = ParseDouble(txtTileHeight.Text, 200),
                HGroutGap = ParseDouble(txtHGroutWidth?.Text, 3),
                VGroutGap = ParseDouble(txtVGroutWidth?.Text, 3),
                PatternType = rbRunningBond.IsChecked == true
                    ? TilePatternType.RunningBond
                    : TilePatternType.Grid,
                RunningBondOffset = offsetPercent / 100.0
            };
        }

        private void BuildUI()
        {
            Title = "磁磚計畫設定";
            Width = 460;
            // 改用 SizeToContent 自動延展高度，避免 UI 元素被遮蔽 (V2.3 UX)
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush(Color.FromRgb(245, 245, 245));

            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 標題
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 快速選擇
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 磁磚設定

            // 灰縫設定列 (過去隱藏，現在需開啟供使用者獨立輸入雙向)
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 3 灰縫設定

            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 4 排列模式 (改為 Auto 防止被壓縮為0)
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 5 按鈕

            // === Row 0: 標題 ===
            var titleBlock = new TextBlock
            {
                Text = "磁磚計畫設定",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(12, 8, 12, 8),
                Foreground = new SolidColorBrush(Color.FromRgb(51, 51, 51))
            };
            Grid.SetRow(titleBlock, 0);
            mainGrid.Children.Add(titleBlock);

            // === Row 1: 快速選擇 ===
            var presetGroup = CreateGroupBox("快速選擇", 1);
            var presetPanel = new StackPanel { Orientation = Orientation.Horizontal };
            presetPanel.Children.Add(new Label { Content = "預設規格：", VerticalAlignment = VerticalAlignment.Center });
            cmbPreset = new ComboBox { Width = 200, Height = 24 };
            cmbPreset.Items.Add("自訂");
            cmbPreset.Items.Add("20 × 20 cm");
            cmbPreset.Items.Add("30 × 30 cm");
            cmbPreset.Items.Add("30 × 60 cm");
            cmbPreset.Items.Add("60 × 60 cm");
            cmbPreset.Items.Add("45 × 90 cm");
            cmbPreset.Items.Add("60 × 120 cm");
            cmbPreset.Items.Add("二丁掛 (60 × 227 mm)");
            cmbPreset.SelectedIndex = 0;
            cmbPreset.SelectionChanged += CmbPreset_SelectionChanged;
            presetPanel.Children.Add(cmbPreset);
            presetGroup.Content = presetPanel;
            mainGrid.Children.Add(presetGroup);

            // === Row 2: 磁磚設定 ===
            var tileGroup = CreateGroupBox("磁磚設定", 2);
            var tileGrid = CreateInputGrid();
            txtTileWidth = AddInputRow(tileGrid, 0, "寬度：", "200", "mm");
            txtTileHeight = AddInputRow(tileGrid, 1, "高度：", "200", "mm");

            // [V3.3] 對調按鈕 (磁磚尺寸)
            var btnSwapSize = new Button
            {
                Content = "🔄 對調長寬",
                Height = 25,
                Width = 80,
                Margin = new Thickness(5, 0, 0, 0),
                ToolTip = "一鍵將磁磚的長度與寬度對調"
            };
            btnSwapSize.Click += BtnSwapSize_Click;
            Grid.SetRow(btnSwapSize, 0);
            Grid.SetRowSpan(btnSwapSize, 2); // 跨兩列
            Grid.SetColumn(btnSwapSize, 3);
            tileGrid.Children.Add(btnSwapSize);

            tileGroup.Content = tileGrid;
            mainGrid.Children.Add(tileGroup);

            // === Row 3: 雙向灰縫設定 ===
            var groutGroup = CreateGroupBox("雙向灰縫設定（分割縫隙）", 3);
            var groutGrid = CreateInputGrid();
            txtHGroutWidth = AddInputRow(groutGrid, 0, "水平灰縫：", "3", "mm");
            txtVGroutWidth = AddInputRow(groutGrid, 1, "垂直灰縫：", "3", "mm");

            // [V3.3] 對調按鈕 (灰縫)
            var btnSwapGrout = new Button
            {
                Content = "🔄 對調灰縫",
                Height = 25,
                Width = 80,
                Margin = new Thickness(5, 0, 0, 0),
                ToolTip = "一鍵將垂直與水平的灰縫寬度對調"
            };
            btnSwapGrout.Click += BtnSwapGrout_Click;
            Grid.SetRow(btnSwapGrout, 0);
            Grid.SetRowSpan(btnSwapGrout, 2); // 跨兩列
            Grid.SetColumn(btnSwapGrout, 3);
            groutGrid.Children.Add(btnSwapGrout);

            groutGroup.Content = groutGrid;
            mainGrid.Children.Add(groutGroup);

            // === Row 4: 排列模式 ===
            var patternGroup = CreateGroupBox("排列模式", 4);
            var patternPanel = new StackPanel();

            rbGrid = new RadioButton
            {
                Content = "正排（對齊排列）",
                IsChecked = true,
                Margin = new Thickness(0, 4, 0, 4),
                FontSize = 13
            };
            rbGrid.Checked += RbPattern_Checked;
            patternPanel.Children.Add(rbGrid);

            rbRunningBond = new RadioButton
            {
                Content = "交丁排（偏移排列）",
                Margin = new Thickness(0, 4, 0, 4),
                FontSize = 13
            };
            rbRunningBond.Checked += RbPattern_Checked;
            patternPanel.Children.Add(rbRunningBond);

            // 交丁偏移設定面板
            pnlOffset = new StackPanel
            {
                Margin = new Thickness(20, 8, 0, 0),
                Visibility = Visibility.Collapsed  // 預設隱藏，選交丁排才顯示
            };

            // 偏移百分比輸入
            var offsetInputPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 8)
            };
            offsetInputPanel.Children.Add(new TextBlock
            {
                Text = "偏移百分比：",
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 13,
                Margin = new Thickness(0, 0, 4, 0)
            });

            txtOffsetInput = new TextBox
            {
                Width = 60,
                Height = 26,
                Text = "50",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            offsetInputPanel.Children.Add(txtOffsetInput);

            offsetInputPanel.Children.Add(new TextBlock
            {
                Text = "% （範圍 1~99）",
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12,
                Margin = new Thickness(4, 0, 0, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 120))
            });
            pnlOffset.Children.Add(offsetInputPanel);

            // 常用百分比快捷按鈕
            var presetOffsetPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 0)
            };
            presetOffsetPanel.Children.Add(new TextBlock
            {
                Text = "常用：",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0),
                FontSize = 12
            });

            // 二丁掛表示方式：「37分」= 比例 3:7 = 偏移 30%
            var presets = new[]
            {
                ("37分 30%", 30),
                ("46分 40%", 40),
                ("55分 50%", 50),
                ("64分 60%", 60),
                ("73分 70%", 70)
            };

            foreach (var (label, value) in presets)
            {
                var btn = new Button
                {
                    Content = label,
                    MinWidth = 55,
                    Height = 28,
                    Margin = new Thickness(2),
                    Tag = value,
                    FontSize = 10,
                    Padding = new Thickness(4, 2, 4, 2)
                };
                btn.Click += BtnPresetOffset_Click;
                presetOffsetPanel.Children.Add(btn);
            }
            pnlOffset.Children.Add(presetOffsetPanel);
            patternPanel.Children.Add(pnlOffset);

            patternGroup.Content = patternPanel;
            mainGrid.Children.Add(patternGroup);

            // === Row 5: 按鈕列 ===
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(8)
            };

            var btnOK = new Button
            {
                Content = "確定",
                Width = 80,
                Height = 30,
                Margin = new Thickness(4),
                IsDefault = true,
                Background = new SolidColorBrush(Color.FromRgb(76, 175, 80)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold
            };
            btnOK.Click += BtnOK_Click;
            buttonPanel.Children.Add(btnOK);

            var btnCancel = new Button
            {
                Content = "取消",
                Width = 80,
                Height = 30,
                Margin = new Thickness(4),
                IsCancel = true
            };
            btnCancel.Click += BtnCancel_Click;
            buttonPanel.Children.Add(btnCancel);

            Grid.SetRow(buttonPanel, 5);
            mainGrid.Children.Add(buttonPanel);

            Content = mainGrid;
        }

        // ===== UI 輔助 =====

        private GroupBox CreateGroupBox(string header, int row)
        {
            var group = new GroupBox
            {
                Header = header,
                Margin = new Thickness(8, 4, 8, 4),
                Padding = new Thickness(8),
                FontSize = 13
            };
            Grid.SetRow(group, row);
            return group;
        }

        private Grid CreateInputGrid()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // [V3.3] 增加第 4 欄給對調按鈕
            return grid;
        }

        private TextBox AddInputRow(Grid grid, int row, string label, string defaultValue, string unit)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var lbl = new Label
            {
                Content = label,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12
            };
            Grid.SetRow(lbl, row);
            Grid.SetColumn(lbl, 0);
            grid.Children.Add(lbl);

            var textBox = new TextBox
            {
                Text = defaultValue,
                Height = 24,
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 2)
            };
            Grid.SetRow(textBox, row);
            Grid.SetColumn(textBox, 1);
            grid.Children.Add(textBox);

            var unitLabel = new Label
            {
                Content = unit,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12
            };
            Grid.SetRow(unitLabel, row);
            Grid.SetColumn(unitLabel, 2);
            grid.Children.Add(unitLabel);

            return textBox;
        }

        // ===== 事件處理 =====

        private void BtnSwapSize_Click(object sender, RoutedEventArgs e)
        {
            string temp = txtTileWidth.Text;
            txtTileWidth.Text = txtTileHeight.Text;
            txtTileHeight.Text = temp;
        }

        private void BtnSwapGrout_Click(object sender, RoutedEventArgs e)
        {
            string temp = txtHGroutWidth.Text;
            txtHGroutWidth.Text = txtVGroutWidth.Text;
            txtVGroutWidth.Text = temp;
        }

        private void CmbPreset_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbPreset.SelectedIndex <= 0) return;

            switch (cmbPreset.SelectedIndex)
            {
                case 1: SetTileDimensions(200, 200); break;
                case 2: SetTileDimensions(300, 300); break;
                case 3: SetTileDimensions(300, 600); break;
                case 4: SetTileDimensions(600, 600); break;
                case 5: SetTileDimensions(450, 900); break;
                case 6: SetTileDimensions(600, 1200); break;
                case 7: SetTileDimensions(60, 227); break;
            }
        }

        private void SetTileDimensions(double width, double height)
        {
            if (txtTileWidth != null) txtTileWidth.Text = width.ToString();
            if (txtTileHeight != null) txtTileHeight.Text = height.ToString();
        }

        private void RbPattern_Checked(object sender, RoutedEventArgs e)
        {
            if (pnlOffset == null) return;
            bool isRunningBond = rbRunningBond.IsChecked == true;
            // 使用 Visibility 而非 IsEnabled/Opacity，確保完全顯示/隱藏
            pnlOffset.Visibility = isRunningBond ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BtnPresetOffset_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn?.Tag != null && int.TryParse(btn.Tag.ToString(), out int value))
            {
                txtOffsetInput.Text = value.ToString();
            }
        }

        private void BtnOK_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateInputs()) return;
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // ===== 驗證 =====

        private bool ValidateInputs()
        {
            // 1. 磁磚規格驗證 (必須為正數)
            if (!ValidateField(txtTileWidth, "磁磚寬度", true)) return false;
            if (!ValidateField(txtTileHeight, "磁磚高度", true)) return false;

            // 2. 灰縫驗證 (不可為負數，允許為0)
            if (!ValidateField(txtHGroutWidth, "水平灰縫寬度", false)) return false;
            if (!ValidateField(txtVGroutWidth, "垂直灰縫寬度", false)) return false;

            // 3. 交丁偏移驗證 (僅在交丁模式下檢查)
            if (rbRunningBond.IsChecked == true)
            {
                if (!double.TryParse(txtOffsetInput.Text, out double offset) ||
                    offset < 1 || offset > 99)
                {
                    MessageBox.Show("請輸入有效的交丁偏移百分比（1~99）。", "輸入錯誤",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    txtOffsetInput.Focus();
                    txtOffsetInput.SelectAll();
                    return false;
                }
            }

            return true;
        }

        private bool ValidateField(TextBox textBox, string fieldName, bool mustBePositive)
        {
            if (textBox == null) return true;

            bool isValid = double.TryParse(textBox.Text, out double value);
            
            if (!isValid || (mustBePositive && value <= 0) || (!mustBePositive && value < 0))
            {
                string requirement = mustBePositive ? "大於 0 的有效數字" : "不小於 0 的有效數字";
                MessageBox.Show($"請在「{fieldName}」輸入{requirement}！", "輸入格式錯誤",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                textBox.Focus();
                textBox.SelectAll();
                return false;
            }
            return true;
        }

        private double ParseDouble(string text, double defaultValue)
        {
            if (double.TryParse(text, out double result) && result > 0)
                return result;
            return defaultValue;
        }
    }
}
