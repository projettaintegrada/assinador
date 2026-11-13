// Projetta Assinador — agente local para Windows
//
// Fica perto do relógio do Windows e atende o site da Projetta em http://localhost:51515.
// O site pede a lista de certificados e, quando o cliente escolhe um, manda os dados
// para assinar. Antes de cada assinatura o programa mostra uma confirmação; com token
// ou cartão A3, o Windows também pede o PIN. A chave privada nunca sai do computador.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ProjettaAssinador
{
    static class Program
    {
        public const int Porta = 51515;
        public static readonly string Versao = Assembly.GetExecutingAssembly().GetName().Version.ToString(3);

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false); // antes de qualquer janela, inclusive avisos
            using (var mutex = new Mutex(true, "ProjettaAssinador.Local", out bool primeiro))
            {
                if (!primeiro)
                {
                    // Outra cópia aberta (normalmente uma versão antiga): oferece trocar por esta
                    var abertos = OutrasInstancias();
                    if (abertos.Count == 0 || MessageBox.Show(
                            "O Projetta Assinador já está aberto.\n\nDeseja fechar o que está aberto e usar esta versão (" + Versao + ")?",
                            "Projetta Assinador", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                        return;
                    foreach (var p in abertos)
                    {
                        try { p.Kill(); p.WaitForExit(5000); } catch { }
                    }
                    try { primeiro = mutex.WaitOne(5000); }
                    catch (AbandonedMutexException) { primeiro = true; } // a cópia fechada ainda segurava a trava
                    if (!primeiro)
                    {
                        MessageBox.Show("Não consegui fechar a outra cópia do Projetta Assinador. Clique com o botão direito no ícone perto do relógio, em Sair, e abra de novo.",
                            "Projetta Assinador", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }
                Application.Run(new Bandeja());
            }
        }

        // Outros processos do assinador (inclui "ProjettaAssinador (1).exe" baixado de novo)
        static List<Process> OutrasInstancias()
        {
            var eu = Process.GetCurrentProcess().Id;
            return Process.GetProcesses()
                .Where(p => { try { return p.Id != eu && p.ProcessName.StartsWith("ProjettaAssinador", StringComparison.OrdinalIgnoreCase); } catch { return false; } })
                .ToList();
        }
    }

    // ---------------- Ícone na bandeja ----------------
    sealed class Bandeja : ApplicationContext
    {
        readonly NotifyIcon icone;
        readonly Servidor servidor;
        readonly Control ui;

        public Bandeja()
        {
            ui = new Control();
            ui.CreateControl();
            var _ = ui.Handle; // garante a janela invisível para mostrar avisos na thread da tela

            var iniciar = new ToolStripMenuItem("Iniciar com o Windows") { CheckOnClick = true, Checked = Inicializacao.Ativa };
            iniciar.CheckedChanged += (s, e) => Inicializacao.Definir(iniciar.Checked);

            var menu = new ContextMenuStrip();
            menu.Items.Add(new ToolStripMenuItem("Projetta Assinador " + Program.Versao) { Enabled = false });
            menu.Items.Add(iniciar);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Sair", null, (s, e) => Sair());

            Icon ico;
            try { ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Shield; }
            catch { ico = SystemIcons.Shield; }
            icone = new NotifyIcon { Icon = ico, Text = "Projetta Assinador", ContextMenuStrip = menu, Visible = true };

            // Na primeira vez, já deixa configurado para abrir junto com o Windows
            if (!Inicializacao.JaConfigurado)
            {
                Inicializacao.Definir(true);
                iniciar.Checked = true;
            }
            else if (Inicializacao.Ativa)
            {
                Inicializacao.Definir(true); // versão nova em outra pasta: passa a abrir esta com o Windows
            }

            servidor = new Servidor(this);
            try
            {
                servidor.Iniciar();
                icone.ShowBalloonTip(5000, "Projetta Assinador",
                    "Pronto. Volte à página de assinatura e escolha o seu certificado.", ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Não consegui iniciar o assinador na porta " + Program.Porta + ".\n\n" + ex.Message,
                    "Projetta Assinador", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // Pergunta ao usuário, na thread da tela, se a assinatura pode acontecer
        public bool Confirmar(string origem, string titular, string tipo)
        {
            Func<bool> perguntar = () =>
            {
                using (var dono = new Form { TopMost = true, ShowInTaskbar = false, StartPosition = FormStartPosition.CenterScreen, Size = new Size(1, 1), Opacity = 0 })
                {
                    dono.Show();
                    dono.Activate();
                    var r = MessageBox.Show(dono,
                        "O site abaixo quer assinar um documento:\n" + origem +
                        "\n\nCertificado: " + titular + " (" + tipo + ")" +
                        "\n\nPermitir a assinatura?",
                        "Projetta Assinador — confirmar assinatura",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
                    return r == DialogResult.Yes;
                }
            };
            return (bool)ui.Invoke(perguntar);
        }

        // Uma única confirmação para assinar vários documentos
        public bool ConfirmarLote(string origem, string titular, string tipo, IList<string> nomes)
        {
            var lista = string.Join("\n", nomes.Take(12).Select(n => "  • " + n));
            if (nomes.Count > 12) lista += "\n  • e mais " + (nomes.Count - 12) + " documento(s)";
            Func<bool> perguntar = () =>
            {
                using (var dono = new Form { TopMost = true, ShowInTaskbar = false, StartPosition = FormStartPosition.CenterScreen, Size = new Size(1, 1), Opacity = 0 })
                {
                    dono.Show();
                    dono.Activate();
                    var r = MessageBox.Show(dono,
                        "O site abaixo quer assinar " + nomes.Count + " documentos:\n" + origem +
                        "\n\n" + lista +
                        "\n\nCertificado: " + titular + " (" + tipo + ")" +
                        "\n\nPermitir a assinatura de todos?",
                        "Projetta Assinador — confirmar assinatura em lote",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
                    return r == DialogResult.Yes;
                }
            };
            return (bool)ui.Invoke(perguntar);
        }

        void Sair()
        {
            servidor.Parar();
            icone.Visible = false;
            icone.Dispose();
            ExitThread();
        }
    }

    // ---------------- Abrir junto com o Windows ----------------
    static class Inicializacao
    {
        const string ChaveRun = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ChaveApp = @"Software\Projetta\Assinador";
        const string Nome = "ProjettaAssinador";

        public static bool Ativa
        {
            get { using (var k = Registry.CurrentUser.OpenSubKey(ChaveRun)) return k?.GetValue(Nome) != null; }
        }

        public static bool JaConfigurado
        {
            get { using (var k = Registry.CurrentUser.OpenSubKey(ChaveApp)) return k?.GetValue("Configurado") != null; }
        }

        public static void Definir(bool ativo)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(ChaveRun))
                {
                    if (ativo) k.SetValue(Nome, "\"" + Application.ExecutablePath + "\"");
                    else k.DeleteValue(Nome, false);
                }
                using (var k = Registry.CurrentUser.CreateSubKey(ChaveApp)) k.SetValue("Configurado", 1);
            }
            catch { /* sem permissão no registro: segue sem iniciar automaticamente */ }
        }
    }

    // ---------------- Certificados do Windows ----------------
    static class Certificados
    {
        public static List<Dictionary<string, object>> Listar()
        {
            var lista = new List<Dictionary<string, object>>();
            using (var loja = new X509Store(StoreName.My, StoreLocation.CurrentUser))
            {
                loja.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
                foreach (var c in loja.Certificates)
                {
                    if (!Utilizavel(c)) continue;
                    lista.Add(new Dictionary<string, object>
                    {
                        ["id"] = c.Thumbprint,
                        ["nome"] = c.GetNameInfo(X509NameType.SimpleName, false),
                        ["emissor"] = c.GetNameInfo(X509NameType.SimpleName, true),
                        ["validade"] = c.NotAfter.ToString("yyyy-MM-dd"),
                        ["tipo"] = Tipo(c),
                        ["cadeia"] = Cadeia(c),
                    });
                }
            }
            return lista.OrderBy(d => (string)d["nome"]).ToList();
        }

        public static X509Certificate2 Achar(string id)
        {
            using (var loja = new X509Store(StoreName.My, StoreLocation.CurrentUser))
            {
                loja.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
                var achados = loja.Certificates.Find(X509FindType.FindByThumbprint, id ?? "", false);
                return achados.Count > 0 && Utilizavel(achados[0]) ? achados[0] : null;
            }
        }

        static bool Utilizavel(X509Certificate2 c)
        {
            if (!c.HasPrivateKey) return false;
            if (c.NotAfter < DateTime.Now || c.NotBefore > DateTime.Now) return false;
            if (c.PublicKey.Oid.Value != "1.2.840.113549.1.1.1") return false; // somente RSA (padrão ICP-Brasil)
            foreach (var ext in c.Extensions)
            {
                if (ext is X509KeyUsageExtension uso &&
                    (uso.KeyUsages & (X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation)) == 0)
                    return false;
            }
            return true;
        }

        // A1 fica no próprio Windows (provedores "Microsoft ..."). Token e cartão usam o provedor do fabricante.
        public static string Tipo(X509Certificate2 c)
        {
            try
            {
                using (var rsa = c.GetRSAPrivateKey())
                {
                    string provedor = "";
                    if (rsa is RSACng cng) provedor = cng.Key.Provider?.Provider ?? "";
                    else if (rsa is RSACryptoServiceProvider csp)
                    {
                        if (csp.CspKeyContainerInfo.HardwareDevice || csp.CspKeyContainerInfo.Removable) return "A3";
                        provedor = csp.CspKeyContainerInfo.ProviderName ?? "";
                    }
                    if (provedor.IndexOf("Smart Card", StringComparison.OrdinalIgnoreCase) >= 0) return "A3";
                    return provedor.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase) ? "A1" : "A3";
                }
            }
            catch { return "A3"; }
        }

        static List<string> Cadeia(X509Certificate2 c)
        {
            var lista = new List<string>();
            using (var cadeia = new X509Chain())
            {
                cadeia.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                cadeia.ChainPolicy.VerificationFlags = X509VerificationFlags.AllFlags;
                try { cadeia.Build(c); } catch { }
                foreach (X509ChainElement el in cadeia.ChainElements)
                    lista.Add(Convert.ToBase64String(el.Certificate.RawData));
            }
            if (lista.Count == 0) lista.Add(Convert.ToBase64String(c.RawData));
            return lista;
        }

        public static byte[] Assinar(X509Certificate2 c, byte[] dados)
        {
            using (var rsa = c.GetRSAPrivateKey())
                return Assinar(rsa, dados);
        }

        public static byte[] Assinar(RSA rsa, byte[] dados)
        {
            {
                if (rsa == null) throw new InvalidOperationException("Este certificado não tem uma chave RSA disponível.");
                try
                {
                    return rsa.SignData(dados, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                }
                catch (CryptographicException) when (rsa is RSACryptoServiceProvider csp && !csp.CspKeyContainerInfo.HardwareDevice)
                {
                    // A1 importado no provedor antigo do Windows não aceita SHA-256 diretamente.
                    // Reabre a mesma chave pelo provedor "RSA and AES", que aceita.
                    var info = csp.CspKeyContainerInfo;
                    var parametros = new CspParameters(24, "Microsoft Enhanced RSA and AES Cryptographic Provider", info.KeyContainerName)
                    {
                        KeyNumber = (int)info.KeyNumber,
                        Flags = CspProviderFlags.UseExistingKey | (info.MachineKeyStore ? CspProviderFlags.UseMachineKeyStore : CspProviderFlags.NoFlags),
                    };
                    using (var aes = new RSACryptoServiceProvider(parametros))
                        return aes.SignData(dados, "SHA256");
                }
            }
        }
    }

    // ---------------- Servidor local ----------------
    sealed class Servidor
    {
        readonly HttpListener http = new HttpListener();
        readonly Bandeja app;
        readonly HashSet<string> origens;
        readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        readonly object travaAssinatura = new object();

        // Lote: depois de uma confirmação, permite N assinaturas com o mesmo certificado,
        // só para o mesmo site e por tempo limitado. A chave fica aberta durante o lote
        // para o token não pedir o PIN a cada documento.
        sealed class Lote
        {
            public string Origem, CertId;
            public int Restantes;
            public DateTime Expira;
            public X509Certificate2 Cert;
            public RSA Chave;
            public void Fechar() { try { Chave?.Dispose(); } catch { } try { Cert?.Dispose(); } catch { } }
        }
        readonly Dictionary<string, Lote> lotes = new Dictionary<string, Lote>();
        const int MaxLote = 200;

        public Servidor(Bandeja app)
        {
            this.app = app;
            origens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var embutida = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "OrigemSite")?.Value;
            if (!string.IsNullOrWhiteSpace(embutida)) origens.Add(embutida.Trim().TrimEnd('/'));

            // Sites extras (opcional): um endereço por linha em %APPDATA%\ProjettaAssinador\origens.txt
            try
            {
                var extra = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ProjettaAssinador", "origens.txt");
                if (File.Exists(extra))
                    foreach (var linha in File.ReadAllLines(extra))
                        if (linha.Trim().StartsWith("http")) origens.Add(linha.Trim().TrimEnd('/'));
            }
            catch { }
        }

        public void Iniciar()
        {
            http.Prefixes.Add("http://localhost:" + Program.Porta + "/");
            http.Start();
            Task.Run(Laco);
        }

        public void Parar()
        {
            try { http.Stop(); http.Close(); } catch { }
        }

        async Task Laco()
        {
            while (http.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await http.GetContextAsync(); }
                catch { break; }
                var _ = Task.Run(() => Atender(ctx));
            }
        }

        void Atender(HttpListenerContext ctx)
        {
            var req = ctx.Request;
            var res = ctx.Response;
            try
            {
                var origem = (req.Headers["Origin"] ?? "").TrimEnd('/');
                if (!origens.Contains(origem))
                {
                    Responder(res, 403, new Dictionary<string, object> { ["erro"] = "Este site não está autorizado a usar o Projetta Assinador." });
                    return;
                }
                res.AddHeader("Access-Control-Allow-Origin", origem);
                res.AddHeader("Vary", "Origin");

                if (req.HttpMethod == "OPTIONS")
                {
                    res.AddHeader("Access-Control-Allow-Methods", "GET, POST");
                    res.AddHeader("Access-Control-Allow-Headers", "Content-Type");
                    res.AddHeader("Access-Control-Allow-Private-Network", "true");
                    res.AddHeader("Access-Control-Max-Age", "600");
                    res.StatusCode = 204;
                    res.Close();
                    return;
                }

                var rota = req.HttpMethod + " " + req.Url.AbsolutePath.TrimEnd('/');
                switch (rota)
                {
                    case "GET /api/status":
                        Responder(res, 200, new Dictionary<string, object> { ["app"] = "Projetta Assinador", ["versao"] = Program.Versao });
                        break;
                    case "GET /api/certificados":
                        Responder(res, 200, new Dictionary<string, object> { ["certificados"] = Certificados.Listar() });
                        break;
                    case "POST /api/assinar":
                        Assinar(req, res, origem);
                        break;
                    case "POST /api/lote":
                        IniciarLote(req, res, origem);
                        break;
                    case "POST /api/lote/fim":
                        EncerrarLote(req, res);
                        break;
                    default:
                        Responder(res, 404, new Dictionary<string, object> { ["erro"] = "Rota não encontrada." });
                        break;
                }
            }
            catch (Exception ex)
            {
                try { Responder(res, 500, new Dictionary<string, object> { ["erro"] = ex.Message }); } catch { }
            }
        }

        Dictionary<string, object> LerPedido(HttpListenerRequest req)
        {
            string corpo;
            using (var leitor = new StreamReader(req.InputStream, Encoding.UTF8)) corpo = leitor.ReadToEnd();
            return json.Deserialize<Dictionary<string, object>>(corpo) ?? new Dictionary<string, object>();
        }

        static string Texto(Dictionary<string, object> pedido, string chave) =>
            pedido.TryGetValue(chave, out var v) ? v as string : null;

        void LimparLotes()
        {
            foreach (var k in lotes.Where(p => p.Value.Expira < DateTime.Now || p.Value.Restantes <= 0).Select(p => p.Key).ToList())
            {
                lotes[k].Fechar();
                lotes.Remove(k);
            }
        }

        void IniciarLote(HttpListenerRequest req, HttpListenerResponse res, string origem)
        {
            var pedido = LerPedido(req);
            var id = Texto(pedido, "id");
            var nomes = (pedido.TryGetValue("arquivos", out var a) ? a as System.Collections.IEnumerable : null)?
                .Cast<object>().Select(o => Convert.ToString(o) ?? "").ToList() ?? new List<string>();
            if (string.IsNullOrEmpty(id) || nomes.Count == 0 || nomes.Count > MaxLote)
            {
                Responder(res, 400, new Dictionary<string, object> { ["erro"] = "Pedido de lote inválido (de 1 a " + MaxLote + " documentos)." });
                return;
            }
            lock (travaAssinatura)
            {
                LimparLotes();
                var cert = Certificados.Achar(id);
                if (cert == null)
                {
                    Responder(res, 404, new Dictionary<string, object> { ["erro"] = "Certificado não encontrado. Se for token ou cartão, confira se está conectado." });
                    return;
                }
                var nome = cert.GetNameInfo(X509NameType.SimpleName, false);
                if (!app.ConfirmarLote(origem, nome, Certificados.Tipo(cert), nomes))
                {
                    cert.Dispose();
                    Responder(res, 403, new Dictionary<string, object> { ["erro"] = "Assinatura recusada no computador.", ["codigo"] = "recusado" });
                    return;
                }
                var token = Convert.ToBase64String(Guid.NewGuid().ToByteArray().Concat(Guid.NewGuid().ToByteArray()).ToArray());
                lotes[token] = new Lote
                {
                    Origem = origem, CertId = id, Restantes = nomes.Count,
                    Expira = DateTime.Now.AddMinutes(15), Cert = cert, Chave = cert.GetRSAPrivateKey(),
                };
                Responder(res, 200, new Dictionary<string, object> { ["lote"] = token });
            }
        }

        void EncerrarLote(HttpListenerRequest req, HttpListenerResponse res)
        {
            var token = Texto(LerPedido(req), "lote");
            lock (travaAssinatura)
            {
                if (token != null && lotes.TryGetValue(token, out var l)) { l.Fechar(); lotes.Remove(token); }
            }
            Responder(res, 200, new Dictionary<string, object> { ["ok"] = true });
        }

        void Assinar(HttpListenerRequest req, HttpListenerResponse res, string origem)
        {
            var pedido = LerPedido(req);
            var id = Texto(pedido, "id");
            var dados = Texto(pedido, "dados");
            var token = Texto(pedido, "lote");
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(dados))
            {
                Responder(res, 400, new Dictionary<string, object> { ["erro"] = "Pedido de assinatura incompleto." });
                return;
            }

            if (!string.IsNullOrEmpty(token))
            {
                lock (travaAssinatura)
                {
                    LimparLotes();
                    if (!lotes.TryGetValue(token, out var lote) || lote.Origem != origem || lote.CertId != id)
                    {
                        Responder(res, 403, new Dictionary<string, object> { ["erro"] = "O lote expirou ou não é válido. Clique em Assinar de novo.", ["codigo"] = "lote" });
                        return;
                    }
                    try
                    {
                        var assinatura = Certificados.Assinar(lote.Chave, Convert.FromBase64String(dados));
                        lote.Restantes--;
                        Responder(res, 200, new Dictionary<string, object> { ["assinatura"] = Convert.ToBase64String(assinatura) });
                    }
                    catch (CryptographicException ex)
                    {
                        Responder(res, 400, new Dictionary<string, object>
                        {
                            ["erro"] = "O certificado não assinou. Se for token ou cartão, confira se está conectado e se o PIN foi digitado corretamente. (" + ex.Message.Trim() + ")",
                        });
                    }
                    return;
                }
            }

            // Uma assinatura por vez, para não empilhar confirmações
            lock (travaAssinatura)
            {
                using (var cert = Certificados.Achar(id))
                {
                    if (cert == null)
                    {
                        Responder(res, 404, new Dictionary<string, object> { ["erro"] = "Certificado não encontrado. Se for token ou cartão, confira se está conectado." });
                        return;
                    }
                    var nome = cert.GetNameInfo(X509NameType.SimpleName, false);
                    if (!app.Confirmar(origem, nome, Certificados.Tipo(cert)))
                    {
                        Responder(res, 403, new Dictionary<string, object> { ["erro"] = "Assinatura recusada no computador.", ["codigo"] = "recusado" });
                        return;
                    }
                    try
                    {
                        var assinatura = Certificados.Assinar(cert, Convert.FromBase64String(dados));
                        Responder(res, 200, new Dictionary<string, object> { ["assinatura"] = Convert.ToBase64String(assinatura) });
                    }
                    catch (CryptographicException ex)
                    {
                        Responder(res, 400, new Dictionary<string, object>
                        {
                            ["erro"] = "O certificado não assinou. Se for token ou cartão, confira se está conectado e se o PIN foi digitado corretamente. (" + ex.Message.Trim() + ")",
                        });
                    }
                }
            }
        }

        void Responder(HttpListenerResponse res, int status, object corpo)
        {
            var bytes = Encoding.UTF8.GetBytes(json.Serialize(corpo));
            res.StatusCode = status;
            res.ContentType = "application/json; charset=utf-8";
            res.ContentLength64 = bytes.Length;
            res.OutputStream.Write(bytes, 0, bytes.Length);
            res.Close();
        }
    }
}
