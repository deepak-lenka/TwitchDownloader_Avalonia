export const metadata = {
  title: "TwitchDownloader UI",
  description: "Simple UI for TwitchDownloaderCLI",
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en">
      <body style={{ fontFamily: 'system-ui, -apple-system, Segoe UI, Roboto, Helvetica, Arial, sans-serif', margin: 0, background: '#0e0e10', color: '#fff' }}>
        <div style={{ maxWidth: 960, margin: '0 auto', padding: 24 }}>
          <header style={{ marginBottom: 16 }}>
            <h1 style={{ margin: 0 }}>TwitchDownloader UI</h1>
            <p style={{ color: '#bdbdbd', marginTop: 8 }}>Paste a VOD/Clip URL or ID, fetch info, and download a clip segment.</p>
          </header>
          {children}
          <footer style={{ marginTop: 48, color: '#8a8a8a' }}>Powered by TwitchDownloaderCLI</footer>
        </div>
      </body>
    </html>
  );
}
