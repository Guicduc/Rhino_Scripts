# ThreadRhino

Plugin para criar roscas métricas físicas e editáveis em faces cilíndricas de sólidos Rhino.

## Comandos

- `RhinoThread`: selecione uma face cilíndrica para adicionar uma rosca.
- `RhinoThreadEdit`: selecione uma peça já processada para editar ou excluir suas roscas.

O plugin aceita sólidos Brep/Extrusion fechados. Malhas, SubD, cones, roscas cosméticas, multientrada e padrões imperiais não fazem parte desta versão.

## Recursos

- Rosca interna ou externa, com detecção automática pela orientação da face.
- Perfil métrico de 60 graus e catálogo M2–M30 com passos grossos e finos.
- A lista ISO mostra apenas tamanhos e passos compatíveis com o diâmetro medido; geometrias fora do catálogo passam automaticamente para `Custom`.
- Sentido direito ou esquerdo.
- Comprimento total ou parcial, offset e inversão do lado inicial.
- Compensação radial para impressão 3D; o padrão é `0,20 mm` aplicado à peça atual.
- Prévia antes de modificar o documento.
- Várias roscas editáveis na mesma peça.
- Persistência da peça-base e dos parâmetros dentro do arquivo `.3dm`.

O catálogo usa dimensões básicas do perfil ISO métrico. Ele não aplica classes industriais de tolerância como 6H/6g e não substitui validação de engenharia para usinagem.

## Compilação

No PowerShell, dentro desta pasta:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -RhinoVersion 7 -RunTests
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -RhinoVersion 8
powershell -NoProfile -ExecutionPolicy Bypass -File .\integration-test.ps1 -RhinoVersion 7
```

O teste de integração abre o Rhino, carrega o `.rhp`, executa casos geométricos sem adicionar objetos ao arquivo e fecha o processo iniciado para o teste. Feche outras sessões do Rhino e confirme que a licença já foi ativada antes de executá-lo.

Arquivos produzidos:

```text
dist\ThreadRhino-Rhino7.rhp
dist\ThreadRhino-Rhino8.rhp
```

## Instalação

1. No Rhino, execute `PluginManager`.
2. Clique em `Install`.
3. Selecione o `.rhp` correspondente à versão do Rhino.
4. Reinicie o Rhino se solicitado.
5. Execute `RhinoThread`.

Este é um plugin Rhino `.rhp`; não deve ser colocado na pasta de componentes do Grasshopper.

## Fluxo de criação

1. Execute `RhinoThread`.
2. Clique diretamente na face cilíndrica. O ponto clicado define a extremidade inicial mais próxima e a fase angular da hélice.
3. Escolha tamanho, passo, sentido, comprimento e compensação.
4. Para uma designação ISO, o diâmetro da face deve corresponder ao diâmetro maior externo ou ao diâmetro menor interno esperado. Use `Custom` quando quiser adaptar o perfil à geometria existente.
5. Confirme em `Aplicar`.

Escalas uniformes mantêm a edição e convertem a designação em `Custom`; espelhamentos invertem o sentido da rosca. Escala não uniforme ou cisalhamento preserva a geometria, mas bloqueia a regeneração paramétrica.
