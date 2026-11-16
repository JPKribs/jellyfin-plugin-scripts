"""Scripts screenshot server: serves the plugin pages, Base and jellyfin-web the way Jellyfin does.

Usage: python3 -I server.py 8873
Base comes from the NuGet cache at the version the plugin's csproj names. JELLYFIN_WEB picks the jellyfin-web folder.
"""
import http.server, json, os, re, sys, urllib.parse
HERE = os.path.dirname(os.path.abspath(__file__))
PLUGIN = os.path.join(HERE, '..', '..', 'Jellyfin.Plugin.Scripts')
REPO = os.path.join(PLUGIN, 'Configuration')
JF = os.environ.get('JELLYFIN_WEB', '/Applications/Jellyfin.app/Contents/Resources/jellyfin-web')
BASEVER = re.search(r'Include="JPKribs\.Jellyfin\.Base"\s+Version="([^"]+)"', open(os.path.join(PLUGIN, 'Jellyfin.Plugin.Scripts.csproj')).read()).group(1)
BASE = os.path.expanduser('~/.nuget/packages/jpkribs.jellyfin.base/%s/assets' % BASEVER)
TYPES = {'.js': 'text/javascript', '.css': 'text/css', '.html': 'text/html', '.json': 'application/json', '.woff2': 'font/woff2', '.woff': 'font/woff', '.svg': 'image/svg+xml', '.png': 'image/png'}

for path, what in ((BASE, 'Base %s (run dotnet restore on the plugin)' % BASEVER), (JF, 'jellyfin-web (set JELLYFIN_WEB)'), (os.path.join(HERE, 'catalog.json'), 'catalog.json (see README.md)')):
    if not os.path.exists(path):
        sys.exit('Missing %s: %s' % (what, path))


def first():
    """The stylesheets jellyfin-web's index.html links, in its order."""
    return re.findall(r'href="([^"?]+\.css)', open(os.path.join(JF, 'index.html')).read())


def stylesheets():
    """jellyfin-web's stylesheets in load order: index.html's two first, the dark theme, then every chunk. A chunk that
    sets the root font to anything but the dashboard's 93% belongs to another layout, such as TV, and is left out."""
    head = first()
    rest = []
    for name in sorted(os.listdir(JF)):
        if not name.endswith('.css') or name in head:
            continue
        sizes = re.findall(r'html\{[^}]*?font-size:([^;}!]+)', open(os.path.join(JF, name), encoding='utf-8', errors='ignore').read())
        if any(size.strip() != '93%' for size in sizes):
            continue
        rest.append(name)
    return head + ['themes/dark/theme.css'] + rest


def links(names):
    return ''.join('<link rel="stylesheet" href="/jf/%s">' % n for n in names)


class H(http.server.BaseHTTPRequestHandler):
    def log_message(self, *a): pass

    def do_GET(self):
        u = urllib.parse.urlparse(self.path)
        q = urllib.parse.parse_qs(u.query)
        if u.path.endswith('configurationpage') and 'name' in q:
            name = q['name'][0]
            if 'jpkribs_shared' in name:
                path = os.path.join(BASE, 'jpkribs_shared' + os.path.splitext(name)[1])
            else:
                path = os.path.join(REPO, name if '.' in name else name + '.html')
        elif u.path.startswith('/jf/'):
            path = os.path.join(JF, u.path[4:])
        else:
            path = os.path.join(HERE, u.path.lstrip('/') or 'index.html')
        if not os.path.isfile(path):
            self.send_error(404); return
        data = open(path, 'rb').read()
        if path.endswith('.html') and os.path.dirname(path) == HERE:
            data = data.replace(b'<!--STYLESHEETS-->', links(stylesheets()).encode())
            data = data.replace(b'<!--STYLESHEETS:INDEX-->', links(first() + ['themes/dark/theme.css']).encode())
        self.send_response(200); self.send_header('Content-Type', TYPES.get(os.path.splitext(path)[1], 'application/octet-stream')); self.send_header('Cache-Control', 'no-store'); self.end_headers(); self.wfile.write(data)


http.server.ThreadingHTTPServer(('127.0.0.1', int(sys.argv[1])), H).serve_forever()
