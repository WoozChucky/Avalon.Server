namespace Avalon.Client.Tester;

partial class TcpForm
{
    /// <summary>
    ///  Required designer variable.
    /// </summary>
    private System.ComponentModel.IContainer components = null;

    /// <summary>
    ///  Clean up any resources being used.
    /// </summary>
    /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }

        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    /// <summary>
    ///  Required method for Designer support - do not modify
    ///  the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent()
    {
        textBox1 = new TextBox();
        button1 = new Button();
        button2 = new Button();
        button3 = new Button();
        textBox2 = new TextBox();
        SuspendLayout();
        // 
        // textBox1
        // 
        textBox1.Location = new Point(12, 288);
        textBox1.Multiline = true;
        textBox1.Name = "textBox1";
        textBox1.Size = new Size(426, 150);
        textBox1.TabIndex = 0;
        // 
        // button1
        // 
        button1.Location = new Point(22, 62);
        button1.Name = "button1";
        button1.Size = new Size(75, 23);
        button1.TabIndex = 1;
        button1.Text = "Connect";
        button1.UseVisualStyleBackColor = true;
        button1.Click += button1_Click;
        // 
        // button2
        // 
        button2.Location = new Point(103, 62);
        button2.Name = "button2";
        button2.Size = new Size(75, 23);
        button2.TabIndex = 2;
        button2.Text = "Disconnect";
        button2.UseVisualStyleBackColor = true;
        button2.Click += button2_Click;
        // 
        // button3
        // 
        button3.Location = new Point(184, 62);
        button3.Name = "button3";
        button3.Size = new Size(75, 23);
        button3.TabIndex = 3;
        button3.Text = "Get PKey";
        button3.UseVisualStyleBackColor = true;
        button3.Click += button3_Click;
        // 
        // textBox2
        // 
        textBox2.Location = new Point(265, 63);
        textBox2.Name = "textBox2";
        textBox2.Size = new Size(173, 23);
        textBox2.TabIndex = 4;
        // 
        // TcpForm
        // 
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(800, 450);
        Controls.Add(textBox2);
        Controls.Add(button3);
        Controls.Add(button2);
        Controls.Add(button1);
        Controls.Add(textBox1);
        Name = "TcpForm";
        Text = "TcpForm";
        Load += Form1_Load;
        ResumeLayout(false);
        PerformLayout();
    }

    #endregion

    private TextBox textBox1;
    private Button button1;
    private Button button2;
    private Button button3;
    private TextBox textBox2;
}