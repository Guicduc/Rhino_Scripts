# EasyText

Comando simples para Rhino:

1. Rode o comando.
2. Clique no ponto onde o texto deve entrar, ou clique e arraste para criar uma caixa de texto.
3. Digite no editor flutuante.
4. Pressione `Ctrl+Enter`.

No clique simples, o script cria um objeto de texto no CPlane da vista ativa com altura ajustada ao zoom da vista. Quanto mais aproximada estiver a vista, menor sera a altura criada no modelo.

No clique e arraste, o script cria texto com quebra de linha dentro da largura arrastada e ajusta a altura da fonte para caber na caixa.

`Esc` cancela. Se o editor perder foco antes de confirmar, ele tambem cancela.

## Como instalar como comando no Rhino

1. No Rhino, rode `Options`.
2. Abra `Aliases`.
3. Crie um alias chamado `EasyText`.
4. Use esta macro, ajustando o caminho se necessario:

```text
_-RunPythonScript "C:\Users\Atelier Bk\Desktop\GUI\SCRIPTS\Rhino\EasyText\EasyText.py"
```

Depois disso, basta digitar `EasyText` no Rhino.
