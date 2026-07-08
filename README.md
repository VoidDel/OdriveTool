# ODrive Custom Upper Computer

基于 **.NET 10 + Avalonia** 的新上位机工程骨架，用于逐步替代原 React/Flask Web GUI。

当前定位：

- 面向 `D:\xiafeng\Project\Odrive\ODrive\Firmware` 中的自定义 ODrive 固件。
- 保留官方 ODrive 0.5.x ASCII 串口兼容通信路径。
- 上位机内部保留多摩川逻辑命名空间：`axis0.tamagawa.*`。
- 真实固件当前通过 `axis0.encoder.*` 暴露多摩川模式和位置，上位机驱动负责映射。
- 已移除 Mock 驱动，仅保留真实串口 ASCII 驱动。

## 项目结构

```text
OdriveUpper.slnx
src/
  OdriveUpper.App/       Avalonia UI / MVVM
  OdriveUpper.Core/      设备抽象、固件 Schema、遥测模型
  OdriveUpper.Drivers/   设备驱动实现：Serial ASCII
tests/
  OdriveUpper.Core.Tests/
```

## 构建

```powershell
dotnet build .\OdriveUpper.slnx
```

## 运行

```powershell
dotnet run --project .\src\OdriveUpper.App\OdriveUpper.App.csproj
```

启动后只扫描真实 ODrive 串口设备。当前 Windows 下优先通过 PnP 匹配 `VID_1209&PID_0D32` 的 ODrive COM 口。

## 测试

```powershell
dotnet test .\OdriveUpper.slnx
```

## 当前真实硬件验证结果

已在当前机器上验证到真实 ODrive：

```text
Port: COM51
Serial: 630037872
Firmware: 0.5.6
Hardware: 3.6.24
vbus_voltage: ~23.8 V
axis0.encoder.config.mode: 261 (0x105 / MODE_UART_ABS_TAMAGAWA)
axis0.encoder.pos_abs: 可读
axis0.encoder.pos_estimate: 可读
axis0.encoder.vel_estimate: 可读
```

官方 Python `odrive.find_any()` 当前会阻塞，但 USB CDC / ASCII 串口协议可正常读写。新上位机因此优先走 `SerialAsciiOdriveDriver`。

## 参数树读写界面

`System` 页面已包含参数树读写界面：

- 连接设备后优先从 `D:\xiafeng\Project\Odrive\ODrive\Firmware\odrive-interface.yaml` 解析完整固件接口。
- 当前可生成 `703` 个参数和 `40` 个命令，覆盖 `config`、`axis0`、`axis1`、`motor`、`controller`、`encoder` 等路径。
- 选中叶子参数后可读取当前值。
- 可写参数支持输入新值并写入，写入成功后自动回读。
- “读取选中分支”会读取当前选中节点下的叶子参数，避免一次性读取 600+ 参数导致串口阻塞。

已用真实 ODrive 验证读取：

```text
schema=ODrive Firmware Interface YAML; properties=703; commands=40
config.uart_a_baudrate=115200
axis0.encoder.pos_abs=56914
axis0.controller.config.pos_gain=200
axis0.motor.config.current_lim 可在树中列出
axis1.encoder.pos_estimate 可在树中列出
```

## 多摩川扩展状态

固件接口目前把多摩川能力挂在 `ODrive.Encoder` 下：

```text
axis0.encoder.config.mode = 261  # MODE_UART_ABS_TAMAGAWA
axis0.encoder.pos_abs            # 单圈绝对位置
axis0.encoder.pos_estimate       # 多圈线性位置估算
axis0.encoder.vel_estimate       # 速度估算
axis0.encoder.error              # 编码器错误位
axis0.encoder.spi_error_rate     # 兼容显示为通信错误率
```

`axis0.tamagawa.*` 目前不是固件真实路径，而是上位机内部逻辑路径。`SerialAsciiOdriveSession` 已做映射：

```text
axis0.tamagawa.absolute_position -> axis0.encoder.pos_abs
axis0.tamagawa.multi_turn_count  -> floor(axis0.encoder.pos_estimate)
axis0.tamagawa.crc_error_count   -> axis0.encoder.spi_error_rate
axis0.tamagawa.warning_status    -> axis0.encoder.error
axis0.tamagawa.error_status      -> axis0.encoder.error
```

`Firmware/odrive-interface.yaml` 的 `ODrive.Encoder.attributes` 已新增以下只读字段，重新生成/编译/烧录固件后可直接通过参数树读取：

```text
axis0.encoder.tamagawa_status
axis0.encoder.tamagawa_alarm_code
axis0.encoder.tamagawa_encoder_id
axis0.encoder.tamagawa_eeprom_page
axis0.encoder.tamagawa_multi_turn
axis0.encoder.tamagawa_error_count
axis0.encoder.tamagawa_last_data_id
axis0.encoder.tamagawa_last_communication
axis0.encoder.tamagawa_timeout_ms
```

`axis1.encoder.*` 下也会自动展开同名字段。当前已烧录设备若还没包含这次接口变更，直接读取这些新字段会返回 `invalid property`；上位机内部 `axis0.tamagawa.*` 逻辑路径会先尝试新字段，失败后回退到旧的兼容路径。

## 后续真实驱动接入建议

如果后续自定义固件继续兼容 ASCII 协议，只需要扩展：

```text
src/OdriveUpper.Drivers/Serial/SerialAsciiOdriveSession.cs
```

如果后续改成自定义二进制协议、CAN、USB Native/Fibre 或专用 RS485 协议，建议新增：

```text
CustomFirmware/
  CustomOdriveDriver.cs
  CustomOdriveSession.cs
```

实现 `OdriveUpper.Core.Devices.IDeviceDriver` 和 `IDeviceSession`，Avalonia UI 不需要大改。
