# Rhino Scripts

Repositorio de ferramentas variadas para Rhino 3D.

## Ativar estilo e layer de uma cota

O script [`Activate_Dimension_Settings.py`](Activate_Dimension_Settings.py) torna ativos o estilo cadastrado e o layer de uma cota selecionada.

1. Selecione uma cota e execute `_RunPythonScript`, escolhendo `Activate_Dimension_Settings.py`. Tambem e possivel executar primeiro e selecionar a cota quando solicitado.
2. Crie as proximas cotas normalmente. Elas usarao o estilo e o layer ativos.

O script aceita cotas lineares, angulares, radiais e de ordenadas; textos e leaders nao sao aceitos. Cancelar a selecao nao altera as configuracoes. Ajustes individuais da cota de referencia nao sao copiados e os estilos existentes nao sao modificados. O layer ativo tambem vale para outros objetos criados depois, ate voce troca-lo. O Rhino pode tornar visivel e desbloquear o layer ao ativa-lo. Se a ativacao do layer falhar, o script tenta restaurar o estilo anterior e informa qualquer falha.

## Toggle Sidebar

O script [`toggle_sidebar.py`](toggle_sidebar.py) alterna a visibilidade do container de paineis que contem o painel Layers, liberando espaco para a viewport.

Como usar:

1. Com o painel Layers aberto e encaixado em um container, execute `_RunPythonScript` no Rhino e selecione `toggle_sidebar.py`.
2. Na primeira execucao, o script salva os identificadores dos paineis abertos nesse container e fecha esses paineis.
3. Execute o mesmo script novamente para reabrir os paineis salvos no container original.

O estado fica em `scriptcontext.sticky` durante a sessao do Rhino. O script nao altera a geometria e nao afeta paineis de outros containers. Se nenhum container for encontrado para Layers, ele nao faz nada.

## Center Rectangle Grasshopper Component

Este componente cria um retangulo central a partir de um ponto.

Arquivos:

- `Grasshopper/CenterRectangleComponent.cs`
- `Grasshopper/CenterRectangle.gha`

Entradas:

- `C` / `Center`: ponto central do retangulo.
- `X` / `X Size`: largura total.
- `Y` / `Y Size`: altura total.

Saidas:

- `R` / `Rectangle`: retangulo centrado.
- `B` / `Boundary`: curva de contorno do retangulo.
- `A` / `Area`: area calculada.

Fluxo equivalente aos componentes nativos da imagem:

1. `X Size` passa por `A * -0.5` e `A * 0.5`.
2. Os dois valores viram o dominio X: `[-X/2, X/2]`.
3. `Y Size` passa por `A * -0.5` e `A * 0.5`.
4. Os dois valores viram o dominio Y: `[-Y/2, Y/2]`.
5. O ponto central vira a origem do plano `World XY`.
6. O componente `Rectangle` recebe esse plano e os dominios X/Y.

Instalacao:

1. Copie `CenterRectangle.gha` para:
   `C:\Program Files\Rhino 7\Plug-ins\Grasshopper\Components`
2. Reinicie Rhino/Grasshopper.
3. Procure por `Center Rectangle` ou `CenterRect` em `Custom > Geometry`.

## Curve Is Vertical Grasshopper Component

Este componente identifica se curvas estao na vertical em relacao ao eixo `World Z`.

Arquivos:

- `Grasshopper/VerticalCurveComponent.cs`
- `Grasshopper/VerticalCurve.gha`

Entradas:

- `C` / `Curves`: lista de curvas para testar.
- `T` / `Tolerance`: tolerancia maxima de variacao em `X/Y`. Se for `0`, usa a tolerancia absoluta do documento Rhino.

Saidas:

- `V` / `Is Vertical`: booleano por curva.
- `VC` / `Vertical Curves`: curvas classificadas como verticais.
- `OC` / `Other Curves`: curvas nao classificadas como verticais.
- `D` / `XY Drift`: maior variacao de bounding box em `X` ou `Y`.
- `H` / `Height`: variacao de bounding box em `Z`.

Regra usada:

1. O componente calcula a bounding box da curva.
2. A curva e vertical quando a maior variacao em `X/Y` e menor ou igual a tolerancia.
3. A curva tambem precisa ter altura em `Z` maior que a tolerancia.

Instalacao:

1. Copie `VerticalCurve.gha` para:
   `C:\Program Files\Rhino 7\Plug-ins\Grasshopper\Components`
2. Reinicie Rhino/Grasshopper.
3. Procure por `Curve Is Vertical` ou `IsVertical` em `Custom > Analysis`.
