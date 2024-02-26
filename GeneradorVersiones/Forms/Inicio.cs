using GeneradorVersiones.Controllers;
using GeneradorVersiones.Forms;
using GeneradorVersiones.Model;
using LibGit2Sharp;
using Microsoft.Build.Execution;
using Octokit;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;

namespace GeneradorVersiones
{
    public partial class Inicio : Form
    {
        private string _ramaActual = "";
        private string _path = "";
        private string _destinationPath = "";
        private string _solutionPath = "";
        private bool _test = true;
        private string _pass = "";
        private GitHubClient _userGitHub;
        private string _tag;

        public Inicio()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
        }

        private void UpdateUsername(string username)
        {
            lblUser.Text = username;
            this.Refresh();
        }

        public void CargarRamaActual(string path)
        {
            _path = path;

            // Obtiene la rama actual del repositoriO
            _ramaActual = GetRepositoryBranch(path);

            //Mostrar rama actual
            lblBranch.Text = _ramaActual;

            //Mostrar commits de la rama actual
            var listCommits = GetLastCommits(path, 10);

            //Insertar últimos commits
            dtgCommits.DataSource = listCommits;
        }

        public string GetRepositoryBranch(string repositoryPath)
        {
            try
            {
                using (var repo = new LibGit2Sharp.Repository(repositoryPath))
                {
                    // Obtiene la referencia de la cabeza (HEAD)
                    var head = repo.Head;

                    // Retorna el nombre de la rama actual
                    return head?.FriendlyName;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al obtener la rama del repositorio: {ex.Message}");
                return "";
            }
        }

        public static List<CommitInfo> GetLastCommits(string repositoryPath, int count)
        {
            try
            {
                List<CommitInfo> commitsList = new List<CommitInfo>();

                using (var repo = new LibGit2Sharp.Repository(repositoryPath))
                {
                    foreach (var commit in repo.Commits.Take(count))
                    {
                        commitsList.Add(new CommitInfo
                        {
                            Sha = commit.Sha,
                            Author = commit.Author.Name,
                            Message = commit.Message
                        });
                    }
                }

                return commitsList;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al obtener los últimos commits: {ex.Message}");
                return null;
            }
        }

        public static bool IsBranchUpToDate(string repositoryPath, string branchName)
        {
            try
            {
                using (var repo = new LibGit2Sharp.Repository(repositoryPath))
                {
                    // Obtener la rama local
                    var localBranch = repo.Branches[branchName];

                    // Obtener la rama remota correspondiente
                    var remoteBranch = localBranch.TrackedBranch;

                    if (remoteBranch == null)
                    {
                        MessageBox.Show("La rama local no está configurada para hacer seguimiento de una rama remota", "Mensaje", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return false;
                    }

                    // Comprobar si la rama local está actualizada con respecto a la remota
                    return localBranch.Tip.Sha == remoteBranch.Tip.Sha;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al comprobar el estado de la rama: {ex.Message}");
                return false;
            }
        }

        private void BuildSolution(string solutionPath)
        {
            var globalProperties = new Dictionary<string, string>
            {
                { "Configuration", "Release" } // Establecer la configuración de compilación
            };

            var buildRequest = new BuildRequestData(solutionPath, globalProperties, null, new[] { "Rebuild" }, null);

            var buildResult = BuildManager.DefaultBuildManager.Build(new BuildParameters(), buildRequest);
            if (buildResult.OverallResult != BuildResultCode.Success)
            {
                throw new Exception("La compilación de la solución ha fallado.");
            }
        }

        private void CopyDlls(string solutionPath, string destinationPath, bool test)
        {
            var currentTime = DateTime.Now;
            var destinationFolderName = currentTime.ToString("yyyyMMdd_HHmmss");

            if (test)
                destinationFolderName += "_TEST";

            // Crea la carpeta de destino con el nombre de la hora actual si no existe
            var destinationFolder = Path.Combine(destinationPath, destinationFolderName);
            Directory.CreateDirectory(destinationFolder);

            foreach (var projectDir in Directory.GetDirectories(solutionPath))
            {
                var outputDir = Path.Combine(projectDir, "bin", "Release");
                if (!Directory.Exists(outputDir))
                    continue;

                foreach (var dllFile in Directory.GetFiles(outputDir, "*.dll"))
                {
                    var targetFileName = Path.GetFileName(dllFile);
                    var destinationFile = Path.Combine(destinationFolder, targetFileName);
                    File.Copy(dllFile, destinationFile, true);
                }
            }
        }


        private void btnObtenerEstadoPath_Click(object sender, EventArgs e)
        {
            CargarRamaActual(txtPath.Text);
            _solutionPath = txtPath.Text;
        }

        private void btnGenerar_Click(object sender, EventArgs e)
        {
            _test = !chkTag.Checked; //Bool que marca si se trata de un test

            //Comprobar si está actualizado 
            if (!IsBranchUpToDate(_path, _ramaActual))
            {
                MessageBox.Show("La rama " + _ramaActual + " no está actualizada.", "Mensaje", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }


            if (!_test)
            {
                if (_ramaActual != "master") //TAG MARCADO, SOLO SE GENERA TAG EN MASTER
                {
                    MessageBox.Show("Sólo se puede generar versión creando un tag desde master.", "Mensaje", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (CheckCommitsAboveTag(_path)) //COMPRUEBA SI HAY COMMITS POR ENCIMA DEL ÚLTIMO TAG
                {
                    MessageBox.Show("No existen nuevos commits desde el último tag.", "Mensaje", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
            }

            if (ObtenerSln(_solutionPath) == null || String.IsNullOrEmpty(_destinationPath))
            {
                MessageBox.Show("Configure correctamente la solución y destino del empaquetado correctamente", "Mensaje", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }


            // Compilar la solución
            BuildSolution(ObtenerSln(_solutionPath));

            // Copiar las DLL generadas al destino del empaquetado
            CopyDlls(_solutionPath, _destinationPath, _test);

            //Generar carpeta con los scripts seleccionados

            if (!_test)
                GenerateAndPushTag(_path);

            //Generar TXT versión tag y usuario responsable
            GenerarTxtVersion(_destinationPath, _test, _tag);

        }

        private void GenerarTxtVersion(string destinationPath, bool test, string tag)
        {
            string contenido = "Usuario: " + Environment.UserName +"\n";
            if (_test)
                contenido += "TEST\n";
            else
                contenido += "TAG GENERADO " + tag + "\n";

            contenido += "Fecha: " + DateTime.Now.ToString();

            // Escribir el contenido en el archivo
            File.WriteAllText(_destinationPath + "Version.txt", contenido);
        }

        private void GenerateAndPushTag(string repositoryPath)
        {
            using (var repo = new LibGit2Sharp.Repository(repositoryPath))
            {
                try
                {
                    // Obtener el último tag
                    var lastTag = repo.Tags.OrderByDescending(x => x.PeeledTarget is LibGit2Sharp.Commit ? ((LibGit2Sharp.Commit)x.PeeledTarget).Committer.When : DateTimeOffset.MinValue).FirstOrDefault();

                    // Determinar el número del nuevo tag
                    string newTagNumber;
                    if (lastTag == null)
                    {
                        // Si no hay tags, empezar desde 1.00
                        newTagNumber = "1.0.0";
                    }
                    else
                    {
                        // Obtener el número del último tag y aumentar su versión
                        var lastTagNumber = lastTag.FriendlyName;
                        var parts = lastTagNumber.Split('.');
                        if (parts.Length != 3 || !int.TryParse(parts[0], out int major) || !int.TryParse(parts[1], out int minor) || !int.TryParse(parts[2], out int build))
                        {
                            Console.WriteLine("El formato del último tag no es válido.");
                            return;
                        }

                        // Incrementar la parte de construcción y formatear el nuevo número del tag
                        build++;
                        newTagNumber = $"{major}.{minor}.{build}";
                    }

                    // Configurar el cliente GitHub con las credenciales del usuario
                    var commit = repo.Head.Tip;
                    var tag = repo.Tags.Add(newTagNumber, commit);

                    var options = new PushOptions
                    {
                        CredentialsProvider = (_url, _user, _cred) =>
                        {
                            return new UsernamePasswordCredentials
                            {
                                Username = "token", // Establecer el nombre de usuario como "token"
                                Password = ConfigurationManager.AppSettings["token"] // Utilizar el token de acceso personal como la contraseña
                            };
                        }
                    };

                    // Hacer push del tag
                    var remote = repo.Network.Remotes["origin"];
                    repo.Network.Push(remote, tag.CanonicalName, options);
                    _tag = newTagNumber;


                }
                catch(Exception ex) 
                { 
                    Console.WriteLine(ex.ToString()); 
                }
            }

   
        }

        private bool CheckCommitsAboveTag(string repositoryPath)
        {
            using (var repo = new LibGit2Sharp.Repository(repositoryPath))
            {
                // Obtiene todos los commits ordenados por fecha
                var commits = repo.Commits.QueryBy(new CommitFilter { SortBy = CommitSortStrategies.Time });

                // Obtiene el último tag
                var lastTag = repo.Tags.OrderByDescending(tag => tag.PeeledTarget is LibGit2Sharp.Commit ? ((LibGit2Sharp.Commit)tag.PeeledTarget).Committer.When : DateTimeOffset.MinValue).FirstOrDefault();

                if (lastTag != null)
                {
                    //Comprobar si hay commits por encima del tag sino avisar con mensaje
                    var lastTagCommit = repo.Lookup<LibGit2Sharp.Commit>(lastTag.PeeledTarget.Sha);

                    if (lastTagCommit == null)
                    {
                        Console.WriteLine($"No se pudo obtener el commit asociado con el último tag '{lastTag.FriendlyName}'.");
                        return false;
                    }
                    // Obtiene el último commit en la rama actual
                    var lastCommit = commits.FirstOrDefault();

                    // Compara los hashes de los commits para determinar si hay commits por encima del último tag
                    return lastCommit?.Sha == lastTagCommit.Sha;
                }
                else
                    return false;
            }

        }

        private string ObtenerSln(string solutionPath)
        {
            if (!String.IsNullOrEmpty(solutionPath))
            {
                string[] solutionFiles = Directory.GetFiles(solutionPath, "*.sln", SearchOption.AllDirectories);

                // Si se encontraron archivos de solución, devuelve el primero encontrado
                if (solutionFiles.Any())
                    return solutionFiles.First();

                return null;
            }
            else
            {
                return null;
            }
        }


        private void btnBuscarProyecto_Click(object sender, EventArgs e)
        {
            _solutionPath = AbrirExplorador(sender, e);
            txtPath.Text = _solutionPath;
        }

        private void btnBuscarDestino_Click(object sender, EventArgs e)
        {
            _destinationPath = AbrirExplorador(sender, e);
            txtDestinoPath.Text = _destinationPath;
        }

        private string AbrirExplorador(object sender, EventArgs e)
        {
            using (FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog())
            {
                folderBrowserDialog.Description = "Selecciona una carpeta como destino:";
                folderBrowserDialog.RootFolder = Environment.SpecialFolder.Desktop;

                DialogResult result = folderBrowserDialog.ShowDialog();

                if (result == DialogResult.OK && !string.IsNullOrWhiteSpace(folderBrowserDialog.SelectedPath))
                    return folderBrowserDialog.SelectedPath;
                else
                    return null;
            }
        }

        private void btnLogIn_Click(object sender, EventArgs e)
        {
            Login loginForm = new Login(this);
            loginForm.Owner = this;
            loginForm.ShowDialog(this);
        }

        public void UpdateCredentials(string user)
        {
            UpdateUsername(user);
        }

        private void dtgCommits_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {

        }

        private void btnBuscarScripts_Click(object sender, EventArgs e)
        {
            ObtenerArchivos();
        }

        private void ObtenerArchivos()
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Multiselect = true; // Permitir selección múltiple de archivos
            openFileDialog.Filter = "Archivos de texto (*.txt)|*.txt|Todos los archivos (*.*)|*.*";

            // Mostrar el diálogo y procesar los archivos seleccionados si el usuario hizo clic en "Aceptar"
            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                foreach (string filename in openFileDialog.FileNames)
                {
                    dtgScripts.Rows.Add(filename);
                }
            }
        }

    }
}
