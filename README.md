# Rhino Scripts

Repositorio de ferramentas variadas para Rhino 3D.

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
