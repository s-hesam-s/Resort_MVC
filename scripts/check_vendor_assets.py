from pathlib import Path
import json, re
root = Path(__file__).resolve().parents[1]
assert not (root / 'Resort.Web/wwwroot/jquery-ajax-unobtrusive').exists(), 'Unused vendor component was restored'
config = json.loads((root / 'Resort.Web/libman.json').read_text(encoding='utf-8'))
assert not any(('jquery-ajax-unobtrusive' in entry['library'] for entry in config['libraries']))
for view in (root / 'Resort.Web/Views').rglob('*.cshtml'):
    assert not re.search('src\\s*=\\s*["\\\'][^"\\\']*jquery\\.unobtrusive-ajax', view.read_text(encoding='utf-8')), str(view)
assert (root / 'Resort.Web/wwwroot/lib/jquery/dist/jquery.min.js').is_file()
print('PASS: unused vendor removed; active jQuery retained; no obsolete view import')
