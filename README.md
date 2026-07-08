# ODrive Custom Upper Computer

基于 **.NET 10 + Avalonia** 的新上位机工程骨架，用于逐步替代原 React/Flask Web GUI。

当前定位：

- 面向 `D:\xiafeng\Project\Odrive\ODrive\Firmware` 中的自定义 ODrive 固件。
- 预留多摩川编码器能力：`axis0.tamagawa.*`。
- 先通过 Mock 驱动验证 UI、设备抽象、Schema 和遥测链路。
- 后续真实通信实现只需要新增 `IDeviceDriver` / `IDeviceSession` 实现。

## 项目结构

```text
OdriveUpper.slnx
src/
  OdriveUpper.App/       Avalonia UI / MVVM
  OdriveUpper.Core/      设备抽象、固件 Schema、遥测模型
  OdriveUpper.Drivers/   设备驱动实现，目前包含 Mock 驱动
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

## 测试

```powershell
dotnet test .\OdriveUpper.slnx
```

## 后续真实驱动接入建议

在 `src/OdriveUpper.Drivers/` 下新增真实驱动，例如：

```text
CustomFirmware/
  CustomOdriveDriver.cs
  CustomOdriveSession.cs
```

实现 `OdriveUpper.Core.Devices.IDeviceDriver` 和 `IDeviceSession`，然后在 `MainWindowViewModel` 中把 `MockOdriveDriver` 替换为真实驱动或驱动注册表。
