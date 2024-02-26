using Octokit;
using System;
using System.Windows.Forms;
using RestSharp;
using RestSharp.Authenticators;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace GeneradorVersiones.Forms
{
    public partial class Login : Form
    {
        private GitHubClient _githubClient;
        private Inicio _padre;

        public Login(Inicio padre)
        {
            InitializeComponent();
            _padre = padre;
        }

        private async void btnLogIn_Click(object sender, EventArgs e)
        {
            string username = txtUsername.Text;
            string password = txtPassword.Text;

            try
            {
                LogInToGitHub(username, password);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Se produjo un error: {ex.Message}");
            }
        }

        private void LogInToGitHub(string username, string password)
        {
            try
            {
                // Aquí debes colocar tu token de acceso personal
                string token = "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";

                // Configurar el cliente Octokit con tu token de acceso personal
                var client = new GitHubClient(new Octokit.ProductHeaderValue("MyApp"));
                client.Credentials = new Credentials(token);

                // Realizar una solicitud a la API de GitHub para obtener la información del usuario autenticado
                var user = client.User.Current().Result;

                _padre.UpdateCredentials(user.Login);

                this.Close();

            }catch(Exception ex)
            {
                MessageBox.Show($"Se produjo un error: {ex.Message}");
            }

        }
    }
}

