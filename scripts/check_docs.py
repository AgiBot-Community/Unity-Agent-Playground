"""Check the maintained documentation matrix and local Markdown file links.

Uses only the standard library. External URLs and heading anchors are not checked.
"""
from pathlib import Path
import re
import subprocess
import sys
from urllib.parse import unquote, urlsplit

ROOT = Path(__file__).resolve().parents[1]
BASES = ('docs/simulator', 'docs/index', 'docs/interface', 'docs/development')
SUFFIXES = ('.md', '.en.md', '.fr.md')
README_GROUPS = (
    ('README.md', 'docs/README.en.md', 'docs/README.fr.md'),
    ('example/README.md', 'example/docs/README.en.md', 'example/docs/README.fr.md'),
)
README_PATHS = {path for group in README_GROUPS for path in group}
LINK = re.compile(r'\[[^\]]*\]\(([^)]+)\)')
FENCE = re.compile(r'^```.*?^```\s*$', re.MULTILINE | re.DOTALL)


def main():
    errors = []
    checked = 0
    maintained = subprocess.check_output(
        ['git', 'ls-files', '--cached', '--others', '--exclude-standard', '-z'],
        cwd=ROOT,
    ).decode('utf-8').split('\0')
    for relative in maintained:
        path = Path(relative)
        if not relative or not (ROOT / path).is_file():
            continue
        if path.suffix.lower() not in ('.md', '.mdx', '.rst'):
            continue
        if relative not in README_PATHS and not relative.startswith(('docs/', 'example/docs/')):
            errors.append(f'Documentation must be under docs/ or example/docs/: {relative}')
        if path.name.lower().startswith('readme') and relative not in README_PATHS:
            errors.append(f'Unexpected README location: {relative}')
    documents = [path for group in README_GROUPS for path in group]
    documents += [base + suffix for base in BASES for suffix in SUFFIXES]
    documents += sorted(set(relative for relative in maintained
                            if relative.startswith(('docs/', 'example/')) and relative.endswith('.md')
                            and (ROOT / relative).is_file()) - set(documents))
    for relative in documents:
        path = ROOT / relative
        if not path.is_file():
            errors.append(f'Missing translation: {relative}')
            continue
        text = path.read_text(encoding='utf-8')
        if not all(language in text for language in ('中文', 'English', 'Français')):
            errors.append(f'Missing language navigation: {relative}')
        prose = FENCE.sub('', text)
        linked_paths = set()
        for match in LINK.finditer(prose):
            raw = match.group(1).strip()
            target = raw[1:raw.index('>')] if raw.startswith('<') else raw.split(' "', 1)[0]
            url = urlsplit(target)
            if url.scheme or url.netloc or not url.path:
                continue
            destination = (path.parent / unquote(url.path)).resolve()
            linked_paths.add(destination)
            if not destination.is_relative_to(ROOT) or not destination.exists():
                errors.append(f'{relative}: broken local link: {target}')
            checked += 1
        for group in README_GROUPS:
            if relative in group:
                for translation in group:
                    if translation != relative and (ROOT / translation).resolve() not in linked_paths:
                        errors.append(f'{relative}: missing language link to {translation}')
        for obsolete in ('sample-project', 'agent_client_doubao.py', 'agent_client_demo.py',
                         'x2-agent.exe', 'x2-agent-demo.exe'):
            if obsolete in text:
                errors.append(f'{relative}: obsolete reference: {obsolete}')
    if errors:
        print('\n'.join(errors))
        return 1
    print(f'OK: {len(documents)} documents, {checked} local links; Chinese README entries, translated docs, language navigation and current entry points checked.')
    return 0


if __name__ == '__main__':
    sys.exit(main())
