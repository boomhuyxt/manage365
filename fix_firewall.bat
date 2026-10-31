@echo off
chcp 65001 >nul
:: Tu dong yeu cau quyen Administrator neu chua co
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo Đang yêu cầu quyền Administrator...
    powershell -Command "Start-Process '%~dpnx0' -Verb RunAs"
    exit /b
)

echo ========================================================
echo Đang gỡ bỏ luật chặn của Windows Defender Firewall...
echo ========================================================

powershell -Command "Get-NetFirewallApplicationFilter -Program 'D:\lt_mobile_qlda\web_app\manage365\bin\debug\net10.0\manage365.exe' | Remove-NetFirewallRule -ErrorAction SilentlyContinue"
powershell -Command "New-NetFirewallRule -DisplayName 'Allow manage365 Backend' -Direction Inbound -Program 'D:\lt_mobile_qlda\web_app\manage365\bin\debug\net10.0\manage365.exe' -Action Allow -Profile Any -Protocol Any"
powershell -Command "Set-NetConnectionProfile -InterfaceAlias 'Wi-Fi' -NetworkCategory Private -ErrorAction SilentlyContinue"

echo.
echo ========================================================
echo [THÀNH CÔNG] Đã mở tường lửa thành công cho Manage365!
echo Bây giờ điện thoại có thể kết nối bình thường qua Wi-Fi:
echo     http://192.168.1.111:5295
echo ========================================================
echo.
pause

