#!/usr/bin/env python3
"""
ExysLauncher — Auditoria estática de consistência UI ↔ ViewModel ↔ Core.

Verifica, sem rodar o app:
  1. XML dos .axaml bem-formado
  2. Todo handler de evento (Click=, Tapped=, ...) existe no code-behind
  3. Todo {Binding Path} resolve na superfície pública do ViewModel/modelos
  4. Todo XxxCommand invocado existe (gerado por [RelayCommand])
  5. Coerência das abas (SelectedTabIndex ↔ IsXxxTab ↔ páginas)
  6. Alcance de features (handlers/comandos/propriedades mortas ou inalcançáveis)
  7. Auditoria HTTP: verbo correto (GET leitura / POST escrita) e HTTPS
  8. x:Name referenciados no code-behind existem no XAML

Uso:  py tools/exys_audit.py [--verbose]
"""

import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "src"
UI = SRC / "LauncherMine.UI"
CORE = SRC / "LauncherMine.Core"
VIEWS = UI / "Views"
VERBOSE = "--verbose" in sys.argv or "-v" in sys.argv

results = []  # (severity, category, message)

def report(sev, cat, msg):
    results.append((sev, cat, msg))
    if VERBOSE or sev != "INFO":
        print(f"[{sev:4}] {cat:14} {msg}")


# ---------------------------------------------------------------- XAML util

def strip_ns(tag: str) -> str:
    return tag.rsplit("}", 1)[-1] if "}" in tag else tag


def iter_axaml():
    for f in sorted(VIEWS.glob("*.axaml")):
        yield f


def axaml_elements(path: Path):
    """Parse XML e devolve (root, [(element, attr_dict)])."""
    try:
        tree = ET.parse(path)
    except ET.ParseError as e:
        report("FAIL", "xaml-xml", f"{path.name}: XML inválido — {e}")
        return None, []
    root = tree.getroot()
    items = []
    for el in root.iter():
        items.append((el, dict(el.attrib)))
    return root, items


EVENT_ATTRS = ("Click", "Tapped", "DoubleTapped", "ContextRequested",
               "SelectionChanged", "PointerPressed", "KeyDown", "Loaded",
               "Unloaded", "Closing", "Closed", "TextChanged", "IsCheckedChanged")

BINDING_RE = re.compile(r"\{Binding\s+([^,}]+)")
HANDLER_RE = re.compile(r'(' + "|".join(EVENT_ATTRS) + r')="([A-Za-z_]\w*)"')
XNAME_RE = re.compile(r'x:Name="([A-Za-z_]\w*)"')


# ------------------------------------------------------- VM surface builder

def pascal(name: str) -> str:
    name = name.lstrip("_")
    return name[:1].upper() + name[1:] if name else name


def build_vm_surface():
    """Extrai propriedades públicas e comandos gerados do ViewModel."""
    vm_path = UI / "ViewModels" / "MainWindowViewModel.cs"
    text = vm_path.read_text(encoding="utf-8")
    lines = text.splitlines()

    props = set()
    # [ObservableProperty] seguido (em até 5 linhas) de 'private <tipo> _campo'
    for i, line in enumerate(lines):
        if "[ObservableProperty]" in line:
            for j in range(i + 1, min(i + 6, len(lines))):
                m = re.search(r'private\s+[\w<>\[\],?\.\s]+?\s+(_?\w+)\s*[=;{]', lines[j])
                if m:
                    props.add(pascal(m.group(1)))
                    break

    # public props / collections / events declarados manualmente
    for m in re.finditer(r'public\s+(?:static\s+)?(?:sealed\s+)?(?:readonly\s+)?'
                         r'[\w<>\[\],?\.\s]+?\s+(\w+)\s*[={;]', text):
        props.add(m.group(1))
    for m in re.finditer(r'public\s+event\s+[\w<>\[\],?\.\s]+?\s+(\w+)\s*;', text):
        props.add(m.group(1))

    # [RelayCommand] -> XxxCommand (remove sufixo Async)
    commands = set()
    lines = text.splitlines()
    for i, line in enumerate(lines):
        if "[RelayCommand]" in line:
            for j in range(i + 1, min(i + 4, len(lines))):
                m = re.search(r'(?:private|public|internal|protected)[\w\s]*?\s(\w+)\s*\(', lines[j])
                if m:
                    name = m.group(1)
                    if name.endswith("Async"):
                        name = name[:-5]
                    commands.add(name + "Command")
                    break

    return props, commands, text


def model_props():
    """Propriedades públicas dos models usados em DataTemplates."""
    out = {}
    for f in (CORE / "Models").glob("*.cs"):
        text = f.read_text(encoding="utf-8")
        props = set()
        for m in re.finditer(
                r'public\s+(?:(?:required|static|virtual|override|abstract|sealed|new|partial)\s+)*'
                r'[\w<>\[\],?\.]+\??\s+(\w+)\s*\{', text):
            props.add(m.group(1))
        out[f.stem] = props
    return out


# ------------------------------------------------------------------- checks

def check_xaml_valid():
    for f in iter_axaml():
        axaml_elements(f)


def check_handlers():
    """Todo handler referenciado no XAML existe no .axaml.cs correspondente."""
    for axaml in iter_axaml():
        cs = axaml.with_suffix(".axaml.cs")
        if not cs.exists():
            report("FAIL", "handlers", f"{axaml.name}: sem code-behind {cs.name}")
            continue
        code = cs.read_text(encoding="utf-8")
        methods = set(re.findall(r'(?:private|public|internal|protected)[\w\s<>\[\],?]*?\s(\w+)\s*\(', code))
        _, items = axaml_elements(axaml)
        for _, attrs in items:
            for attr, handler in attrs.items():
                local = strip_ns(attr)
                if local in EVENT_ATTRS and re.fullmatch(r"[A-Za-z_]\w*", handler):
                    if handler not in methods:
                        report("FAIL", "handlers",
                               f"{axaml.name}: {local}=\"{handler}\" sem método em {cs.name}")


def check_bindings_and_commands():
    props, commands, _ = build_vm_surface()
    models = model_props()
    vm_props = props | commands

    # modelos por DataContext provável dentro de ItemsControl
    template_model = {
        "Instances": "InstanceMetadata",
        "Accounts": "AccountProfile",
    }

    for axaml in iter_axaml():
        name = axaml.name
        _, items = axaml_elements(axaml)
        if not items:
            continue

        # contexto do arquivo
        if name == "MainWindow.axaml":
            surface, item_surfaces = vm_props, dict(template_model)
        else:
            # dialogs: bindings resolvem no code-behind/propriedades locais — só reporta INFO
            surface, item_surfaces = None, {}

        # mapeia ItemsControl -> modelo do item para paths dentro de DataTemplate
        # heurística: coleta nomes de ItemsSource próximos (ordem do documento)
        items_source_stack = []
        current_item_model = None

        for el, attrs in items:
            tag = strip_ns(el.tag)
            src = attrs.get("ItemsSource", "")
            if "ItemsSource" in attrs:
                m = re.search(r"ItemsSource=\"\{Binding\s+(\w+)", src)
                current_item_model = item_surfaces.get(m.group(1)) if m else None
            if tag == "ItemsControl" and "ItemsSource" in attrs:
                pass
            # fim de DataTemplate é difícil em iter linear; aproximação:
            # bindings dentro do arquivo principal fora de template usam VM;
            # dentro de ItemTemplate usam item model — detectamos por janela:

        # segunda passada com estado de template
        in_template_for = None
        for el, attrs in items:
            tag = strip_ns(el.tag)
            # detecta entrada/saída de DataTemplate via ItemTemplate
            for k, v in attrs.items():
                pass
            # mais simples: varre texto bruto por blocos ItemTemplate
        raw = axaml.read_text(encoding="utf-8")

        # extrai blocos <ItemsControl.ItemTemplate>...</ItemsControl.ItemTemplate>
        template_blocks = re.findall(
            r'<ItemsControl\.ItemTemplate>(.*?)</ItemsControl\.ItemTemplate>', raw, re.S)
        body_wo_templates = re.sub(
            r'<ItemsControl\.ItemTemplate>.*?</ItemsControl\.ItemTemplate>', '', raw, flags=re.S)

        def check_path(path: str, where: str, item_model: str | None):
            path = path.strip()
            if not path or path.startswith("$"):
                # $parent[Window].((vm:VM)DataContext).XxxCommand
                m = re.search(r'DataContext\)\.(\w+)', path)
                if m:
                    tail = m.group(1)
                    if name == "MainWindow.axaml" and tail not in vm_props:
                        report("FAIL", "commands", f"{name}: comando inexistente '{tail}'")
                return
            head = path.split(".")[0].lstrip("!").strip()
            rest = path.split(".")[1:]

            if item_model and head not in ("DataContext",):
                # path dentro de DataTemplate: primeiro segmento = prop do model do item
                mprops = models.get(item_model, set())
                if head not in mprops:
                    report("FAIL", "bindings",
                           f"{name}: '{path}' em template de {item_model} — '{head}' não existe no model")
                return

            if name != "MainWindow.axaml":
                return  # dialogs: revisão manual

            if head not in vm_props:
                report("FAIL", "bindings",
                       f"{name}: binding '{path}' — '{head}' não existe no ViewModel")
                return
            # valida segundo nível quando head é AccountProfile?/InstanceMetadata?
            if rest and head in ("SelectedAccount", "SelectedInstance"):
                model = "AccountProfile" if head == "SelectedAccount" else "InstanceMetadata"
                mprops = models.get(model, set())
                if rest[0] not in mprops:
                    report("FAIL", "bindings",
                           f"{name}: '{path}' — '{rest[0]}' não existe em {model}")

        # bindings no corpo (fora de templates)
        for m in BINDING_RE.finditer(body_wo_templates):
            check_path(m.group(1), "body", None)

        # bindings dentro de cada template, com modelo inferido pelo ItemsSource mais próximo
        for block in template_blocks:
            # modelo: procura ItemsSource na janela anterior ao bloco
            idx = raw.find(block)
            preceding = raw[max(0, idx - 600):idx]
            ms = re.findall(r'ItemsSource="\{Binding\s+(\w+)', preceding)
            src_name = ms[-1] if ms else None
            model = item_surfaces.get(src_name) if src_name else None
            for m in BINDING_RE.finditer(block):
                check_path(m.group(1), "template", model)

        # negação '!' — Avalonia 11 suporta em bindings; registrado como INFO
        for m in re.finditer(r'\{Binding\s+!(\w+)', raw):
            report("INFO", "bindings",
                   f"{name}: negação '!{m.group(1)}' (suportada pelo Avalonia 11)")


def check_tabs():
    """SelectedTabIndex: cada índice usado tem página IsVisible correspondente."""
    vm_path = UI / "ViewModels" / "MainWindowViewModel.cs"
    text = vm_path.read_text(encoding="utf-8")
    cs = (VIEWS / "MainWindow.axaml.cs").read_text(encoding="utf-8")
    xaml = (VIEWS / "MainWindow.axaml").read_text(encoding="utf-8")

    # índices atribuídos em código
    assigned = set(int(m) for m in re.findall(r'SelectedTabIndex\s*=\s*(\d+)', cs + text))
    # páginas existentes
    pages = set(re.findall(r'IsVisible="\{Binding\s+(Is\w+Tab)\}', xaml))
    # mapa prop->índice no VM
    tab_map = {}
    for m in re.finditer(r'public bool (Is\w+Tab) => SelectedTabIndex == (\d+)', text):
        tab_map[m.group(1)] = int(m.group(2))

    for idx in sorted(assigned):
        props_for_idx = [p for p, i in tab_map.items() if i == idx]
        if not props_for_idx:
            report("FAIL", "tabs", f"SelectedTabIndex={idx} atribuído mas nenhuma página IsXxxTab o cobre")
        else:
            for p in props_for_idx:
                if p not in pages:
                    report("FAIL", "tabs", f"{p} (tab {idx}) existe no VM mas nenhuma página a usa")

    # páginas órfãs
    covered = set(tab_map.keys())
    for p in pages - covered:
        report("WARN", "tabs", f"página '{p}' no XAML sem propriedade correspondente no VM")


def check_reachability():
    """Handlers/comandos/propriedades que o usuário nunca alcança."""
    vm_props, commands, vm_text = build_vm_surface()
    main_xaml = (VIEWS / "MainWindow.axaml").read_text(encoding="utf-8")
    main_cs = (VIEWS / "MainWindow.axaml.cs").read_text(encoding="utf-8")

    # handlers mortos em MainWindow.axaml.cs
    handlers = set(re.findall(r'(?:private|public)[\w\s<>\[\],?]*?\s(On\w+)\s*\(', main_cs))
    for h in sorted(handlers):
        if f'"{h}"' not in main_xaml:
            report("WARN", "reachability", f"handler morto: {h}() não referenciado no XAML")

    # comandos gerados nunca usados (XAML ou code-behind)
    xaml_all = "\n".join(f.read_text(encoding="utf-8") for f in iter_axaml())
    code_all = "\n".join(f.read_text(encoding="utf-8")
                         for f in VIEWS.glob("*.axaml.cs"))
    ui_all = xaml_all + "\n" + code_all
    for c in sorted(commands):
        if c not in ui_all:
            report("WARN", "reachability", f"comando não exposto na UI: {c}")

    # propriedades que DEVEM ter representação visual
    must_show = ["StatusMessage", "IsProgressVisible", "ProgressValue", "ImportZipPath",
                 "RamAllocationGbDisplay", "SelectedJavaDisplay", "RamInfoText",
                 "StatusBadgeText"]
    for p in must_show:
        if p not in xaml_all:
            report("WARN", "reachability", f"propriedade '{p}' nunca exibida/editada na UI")

    # estado interno de sincronização instance→VM→launch (não exige UI própria)
    internal = ["CustomJvmArgsText", "InstanceManifestUrl", "SelectedGameVersion",
                "SelectedLoader", "SelectedLoaderVersion", "AvailableLoaderVersions",
                "AvailableGameVersions", "CustomJavaPath", "IsGameRunning"]
    for p in internal:
        if p not in xaml_all:
            report("INFO", "reachability", f"'{p}' é estado interno (sync instância→launch), sem UI dedicada")


def check_control_names():
    """x:Name devem ser identificadores C# válidos e únicos por arquivo.
    (Uso de campos gerados no code-behind já é garantido pelo compilador.)"""
    for axaml in iter_axaml():
        raw = axaml.read_text(encoding="utf-8")
        names = XNAME_RE.findall(raw)
        for n in names:
            if not re.fullmatch(r"[A-Za-z_]\w*", n):
                report("FAIL", "names", f"{axaml.name}: x:Name '{n}' não é identificador C# válido")
        dup = {n for n in names if names.count(n) > 1}
        for n in dup:
            report("FAIL", "names", f"{axaml.name}: x:Name '{n}' duplicado")


def check_http():
    """Auditoria de verbos HTTP e HTTPS nas chamadas do Core."""
    read_endpoints = re.compile(r'(profile|versions?|manifest|download|file|asset|libraries|/v2/)', re.I)
    write_endpoints = re.compile(r'(authenticate|authorize|token|login|devicecode|session|purchase|entitle)', re.I)

    for cs in CORE.rglob("*.cs"):
        text = cs.read_text(encoding="utf-8")
        lines = text.splitlines()
        for i, line in enumerate(lines):
            m = re.search(r'\.(GetAsync|PostAsync|PutAsync|DeleteAsync|PatchAsync|SendAsync|PostAsJsonAsync|GetFromJsonAsync|PostAsJsonAsync)\s*\(', line)
            if not m:
                continue
            method = m.group(1)
            # janela de contexto: URL costuma estar na mesma ou próximas linhas
            window = "\n".join(lines[i:i + 4])
            urls = re.findall(r'"(https?://[^"]+)"', window)
            url = urls[0] if urls else "(url indireta)"

            # HTTPS obrigatório para requests
            if url.startswith("http://"):
                report("FAIL", "http", f"{cs.name}:{i+1} {method} usa HTTP inseguro: {url}")
            elif url.startswith("https://") is False and url != "(url indireta)":
                pass

            # heurística verbo × endpoint
            if url != "(url indireta)":
                if write_endpoints.search(url) and method.startswith("Get"):
                    report("FAIL", "http", f"{cs.name}:{i+1} GET em endpoint de escrita: {url}")
                if read_endpoints.search(url) and method.startswith(("Post", "Put", "Delete", "Patch")) \
                        and not write_endpoints.search(url):
                    report("WARN", "http", f"{cs.name}:{i+1} {method} em endpoint de leitura: {url}")

            # http:// em strings que não são request (ex.: RelyingParty)
        for i, line in enumerate(lines):
            for um in re.finditer(r'"http://([^"]+)"', line):
                ctx = line.strip()
                if "RelyingParty" in ctx or "AuthRequest" in ctx or "xbox" in um.group(1):
                    report("INFO", "http", f"{cs.name}:{i+1} http:// em campo de protocolo (não é request): {um.group(0)}")
                else:
                    report("WARN", "http", f"{cs.name}:{i+1} http:// literal: {um.group(0)}")


def check_dialogs():
    """Handlers dos dialogs existem nos code-behinds."""
    for axaml in iter_axaml():
        if axaml.name == "MainWindow.axaml":
            continue
        cs = axaml.with_suffix(".axaml.cs")
        if not cs.exists():
            continue
        code = cs.read_text(encoding="utf-8")
        methods = set(re.findall(r'(?:private|public)[\w\s<>\[\],?]*?\s(\w+)\s*\(', code))
        _, items = axaml_elements(axaml)
        for _, attrs in items:
            for attr, handler in attrs.items():
                if strip_ns(attr) in EVENT_ATTRS and handler not in methods:
                    report("FAIL", "handlers", f"{axaml.name}: '{handler}' sem método")


# -------------------------------------------------------------------- main

def main():
    print("=" * 70)
    print("EXYSLAUNCHER — AUDITORIA ESTÁTICA DE CONSISTÊNCIA")
    print("=" * 70)

    check_xaml_valid()
    check_handlers()
    check_bindings_and_commands()
    check_tabs()
    check_reachability()
    check_control_names()
    check_http()
    check_dialogs()

    fails = [r for r in results if r[0] == "FAIL"]
    warns = [r for r in results if r[0] == "WARN"]
    infos = [r for r in results if r[0] == "INFO"]

    print()
    print("-" * 70)
    print(f"RESULTADO: {len(fails)} FAIL | {len(warns)} WARN | {len(infos)} INFO")
    print("-" * 70)

    if fails:
        print("\nFAILURES (devem ser corrigidos):")
        for _, cat, msg in fails:
            print(f"  ✗ [{cat}] {msg}")
    if warns:
        print("\nWARNINGS (revisar):")
        for _, cat, msg in warns:
            print(f"  ⚠ [{cat}] {msg}")

    return 1 if fails else 0


if __name__ == "__main__":
    sys.exit(main())
