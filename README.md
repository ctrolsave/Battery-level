# Battery Level

<img src="icono.png" width="96" align="right" alt="Ícono de Battery Level">

Battery Level es una app chiquita para Windows 10 y 11. Muestra en la barra de tareas, al lado de la bandeja, cuánta batería les queda a tus dispositivos.

- **Dispositivos Razer** (mouse, auriculares, teclados inalámbricos): con Razer Synapse abierto, el dato sale de lo que Synapse va anotando. Sin Synapse, se lo pide directo al dongle o al cable (por ahora solo el Basilisk V3 Pro 35K).
- **Cualquier dispositivo Bluetooth conectado que informe su batería a Windows**: auriculares, parlantes, joysticks, mouse, teclados.

Cuando conectás un dispositivo con batería, aparece solo a los pocos segundos.

## Cómo se ve

Cada dispositivo muestra su logo (el de Razer, auriculares, parlante o joystick), una batería dibujada y el porcentaje.

- La batería es blanca o negra según el tema de Windows.
- Se pone amarilla al 30% o menos y roja al 15% o menos, con aviso.
- Cuando carga, se pone verde, late y muestra un rayo.
- Con clic izquierdo se abre una ventanita con el detalle de todos los dispositivos.
- Con clic derecho elegís qué dispositivos se ven en la barra. Los que saques ahí siguen apareciendo en los íconos ocultos (`^`), donde están todos siempre.
- El panel se oculta con juegos o videos a pantalla completa.

## Instalar

1. Bajá `BatteryLevel-Setup.exe` de la sección [Releases](../../releases).
2. Abrilo y tocá **Instalar**. Se instala solo para tu usuario y no pide permisos de administrador.

Como el instalador no está firmado digitalmente, Windows SmartScreen puede avisar "Windows protegió tu PC". Para seguir, tocá **Más información** y después **Ejecutar de todas formas**.

Para desinstalar: *Configuración > Aplicaciones > Aplicaciones instaladas > Battery Level > Desinstalar*.

El instalador también acepta estas opciones: `/silencioso`, `/sin-inicio` y `/sin-abrir`.

## Firma digital y copias modificadas

`BatteryLevel.exe` y `BatteryLevel-Setup.exe` están firmados digitalmente por **Ctrl + Save Design Studio**. Cada vez que se abren, revisan su propia firma. Si alguien los modificó, les sacó la firma o los firmó con otro certificado, muestran un aviso y no se abren. El instalador también revisa el programa que trae adentro antes de instalarlo.

- Huella del certificado: `56BDEEC1FB847B3C2A684D7D0E3F7FB26E620A6C` (el certificado público está en `CtrlSave-Design-Studio.cer`).
- Para revisarlo a mano: clic derecho en el .exe > *Propiedades* > *Firmas digitales*.
- Los hashes SHA-256 de cada versión están en `SHA256SUMS.txt`, en Releases.

El certificado es propio, no lo emitió una autoridad comercial. Por eso Windows lo muestra como "no confiable" o "editor desconocido". Igual sirve para detectar cualquier cambio en el archivo.

**Ctrl + Save Design Studio no se hace cargo de copias modificadas** ni de lo que puedan hacer. Bajá el instalador solo de este repositorio o pedíselo directamente al estudio.

## Compilar

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

Usa el `csc` de .NET Framework 4 que ya trae Windows, así que no hace falta instalar nada. El resultado queda en `dist\`: `BatteryLevel.exe`, `BatteryLevel-Setup.exe` (que lleva la app adentro) y `SHA256SUMS.txt`. Para firmar hace falta el certificado de Ctrl + Save Design Studio con su clave privada, que está solo en la PC del estudio. Sin él, la compilación se corta.

`BatteryLevel.exe --prueba` escribe lo que lee en `prueba.txt`.

## Aviso

Razer y Razer Synapse son marcas de Razer Inc. Este proyecto no está afiliado a Razer. La app no incluye el logo de Razer: lo toma, si existe, del Razer Synapse instalado en la PC.

---

Proyecto elaborado por Ctrl + Save Design Studio

Copyright (c) 2026 Ctrl + Save Design Studio. Todos los derechos reservados.
