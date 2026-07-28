<#
.SYNOPSIS
    Giả lập máy chủ VNPay gọi IPN tới ứng dụng ĐANG CHẠY THẬT.

.DESCRIPTION
    Bộ test xUnit (PaymentIpnTests) chạy trong WebApplicationFactory với một HashSecret
    do chính test bơm vào. Nó chứng minh nghiệp vụ đúng, nhưng KHÔNG chạm tới:

      - khoá thật đang nằm trong User Secrets / biến môi trường,
      - cấu hình thật trong appsettings.json,
      - và pipeline HTTP thật của `dotnet run`.

    Script này lấp đúng khoảng trống đó. Nó cũng chính là công cụ để thử tay khi bạn có
    khoá sandbox thật và đã dựng ngrok - lúc đó chỉ cần đổi -BaseUrl.

    Phép ký được VIẾT LẠI ở đây, không gọi VnPayService: script đóng vai ĐỐI TÁC. Dùng
    chính code đang kiểm để tạo dữ liệu đầu vào thì chỉ chứng minh code nhất quán với
    chính nó, kể cả khi cả hai chiều cùng sai.

.PARAMETER OrderId
    Đơn hàng dùng để thử. BẮT BUỘC đang ở trạng thái Pending.

.PARAMETER Reset
    Sau khi chạy xong, đưa đơn về Pending và xoá bản ghi Payment của nó, để chạy lại
    được. GHI VÀO DB - chỉ dùng trên máy dev.

.EXAMPLE
    ./scripts/test-vnpay-ipn.ps1 -OrderId 367 -Reset
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [int]$OrderId,

    [string]$BaseUrl = 'http://localhost:5231',

    # Mặc định đọc từ User Secrets của MiniMart.Web - đúng nguồn mà ứng dụng đang đọc.
    [string]$HashSecret,

    [string]$SqlInstance = 'localhost\SQLEXPRESS',

    [switch]$Reset
)

$ErrorActionPreference = 'Stop'

$sqlcmd = 'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\180\Tools\Binn\SQLCMD.EXE'
$duAn = Join-Path $PSScriptRoot '..' 'MiniMart.Web'

# ─────────────────────────── Helper ───────────────────────────

function Invoke-Sql([string]$Sql) {
    $ketQua = & $sqlcmd -S $SqlInstance -E -d MiniMart -C -h -1 -W -Q "SET NOCOUNT ON; $Sql"

    if ($LASTEXITCODE -ne 0) { throw "sqlcmd lỗi: $ketQua" }

    return ($ketQua | Where-Object { $_ -and $_.Trim() }) -join "`n"
}

function Get-HashSecret {
    if ($HashSecret) { return $HashSecret }

    $dong = & dotnet user-secrets list --project $duAn 2>$null |
        Where-Object { $_ -like 'VnPay:HashSecret = *' }

    if (-not $dong) {
        throw @'
Không đọc được VnPay:HashSecret từ User Secrets.
Chạy: dotnet user-secrets set "VnPay:HashSecret" "<khoa-sandbox>" --project MiniMart.Web
Hoặc truyền thẳng: -HashSecret "<khoa>"
'@
    }

    return ($dong -split ' = ', 2)[1]
}

<#
    Ký ĐÚNG quy tắc VNPay, viết lại độc lập với C#:
      1. bỏ vnp_SecureHash / vnp_SecureHashType
      2. sắp xếp khoá theo ORDINAL
      3. ghép key=urlencode(value) nối bằng &
      4. HMAC-SHA512 với HashSecret, in hex thường

    Bước 2 dùng [StringComparer]::Ordinal chứ KHÔNG dùng `Sort-Object`: Sort-Object so
    sánh theo culture, đúng cái mà quy ước C# cấm. Sai chỗ này thì script tự tạo ra một
    chữ ký khác server và mọi case đều "sai chữ ký" - một kết quả trông rất thuyết phục
    mà hoàn toàn vô nghĩa.
#>
function New-VnPaySignature([hashtable]$ThamSo, [string]$Secret) {
    $khoa = [string[]]($ThamSo.Keys | Where-Object { $_ -notin 'vnp_SecureHash', 'vnp_SecureHashType' })
    [Array]::Sort($khoa, [System.StringComparer]::Ordinal)

    $cap = foreach ($k in $khoa) {
        if ([string]::IsNullOrEmpty($ThamSo[$k])) { continue }
        '{0}={1}' -f $k, [System.Net.WebUtility]::UrlEncode($ThamSo[$k])
    }

    $duLieu = $cap -join '&'

    $hmac = [System.Security.Cryptography.HMACSHA512]::new([Text.Encoding]::UTF8.GetBytes($Secret))
    try {
        $bam = $hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($duLieu))
    }
    finally { $hmac.Dispose() }

    return @{
        Query     = $duLieu
        Signature = (($bam | ForEach-Object { $_.ToString('x2') }) -join '')
    }
}

function Invoke-Ipn([hashtable]$ThamSo, [string]$Secret, [string]$ChuKyDeCap) {
    $ky = New-VnPaySignature -ThamSo $ThamSo -Secret $Secret
    $chuKy = if ($ChuKyDeCap) { $ChuKyDeCap } else { $ky.Signature }

    $url = '{0}/Payment/IpnAction?{1}&vnp_SecureHash={2}' -f $BaseUrl, $ky.Query, $chuKy

    try {
        return Invoke-RestMethod -Uri $url -Method Get -TimeoutSec 20
    }
    catch {
        throw "Không gọi được $BaseUrl. Ứng dụng đã chạy chưa? (dotnet run --project MiniMart.Web --launch-profile http)`n$_"
    }
}

$script:soLoi = 0

function Assert-Case([string]$Ten, [string]$MongDoi, $ThucTe, [string]$GhiChu) {
    $ok = $ThucTe.rspCode -eq $MongDoi

    if (-not $ok) { $script:soLoi++ }

    [pscustomobject]@{
        'Case'    = $Ten
        'Mong doi'= $MongDoi
        'Thuc te' = '{0} ({1})' -f $ThucTe.rspCode, $ThucTe.message
        'Ket qua' = if ($ok) { 'PASS' } else { 'FAIL' }
        'Y nghia' = $GhiChu
    }
}

# ─────────────────────────── Chuẩn bị ───────────────────────────

$secret = Get-HashSecret
Write-Host "HashSecret: $($secret.Substring(0, [Math]::Min(6, $secret.Length)))... (đọc từ User Secrets)" -ForegroundColor DarkGray

$thongTin = (Invoke-Sql "SELECT CONCAT(CAST(TotalAmount AS varchar(30)), '|', Status) FROM Orders WHERE Id = $OrderId").Trim()

if (-not $thongTin) { throw "Không có đơn hàng Id = $OrderId trong DB." }

$tongTien, $trangThai = $thongTin -split '\|'

if ($trangThai -ne 'Pending') {
    throw "Đơn $OrderId đang ở trạng thái '$trangThai', cần 'Pending'. Chạy lại với -Reset, hoặc chọn đơn khác."
}

# Số tiền gửi sang VNPay là số ĐÃ NHÂN 100, không có phần thập phân.
$soTienVnPay = [long]([decimal]::Parse($tongTien, [Globalization.CultureInfo]::InvariantCulture) * 100)

Write-Host "Đơn #$OrderId - tổng $tongTien đ - vnp_Amount = $soTienVnPay" -ForegroundColor DarkGray
Write-Host ''

function New-ThamSo([long]$Amount, [string]$TxnRef, [string]$ResponseCode = '00', [string]$TransactionStatus = '00') {
    return @{
        vnp_Amount            = "$Amount"
        vnp_BankCode          = 'NCB'
        vnp_OrderInfo         = "Thanh toan don hang $TxnRef"
        vnp_ResponseCode      = $ResponseCode
        vnp_TmnCode           = 'SCRIPT_TMN'
        vnp_TransactionNo     = '14200000'
        vnp_TransactionStatus = $TransactionStatus
        vnp_TxnRef            = $TxnRef
    }
}

# ─────────────────────────── Các case ───────────────────────────
#
# Thứ tự có chủ đích: mọi case TỪ CHỐI chạy trước, case thành công chạy sau cùng.
# Case thành công đổi trạng thái đơn, nên chạy nó trước sẽ khiến các case sau nhận 02
# thay vì mã đang muốn kiểm.

$ketQua = @()

$ketQua += Assert-Case 'Sai chữ ký' '97' `
    (Invoke-Ipn (New-ThamSo $soTienVnPay "$OrderId") $secret ('a' * 128)) `
    'Chữ ký không tính được nếu không có khoá'

$ketQua += Assert-Case 'Sai khoá bí mật' '97' `
    (Invoke-Ipn (New-ThamSo $soTienVnPay "$OrderId") 'KHOA_HOAN_TOAN_KHAC') `
    'Ký bằng khoá khác cũng là chữ ký sai'

$ketQua += Assert-Case 'Sai số tiền (1 đồng)' '04' `
    (Invoke-Ipn (New-ThamSo 100 "$OrderId") $secret) `
    'CHỮ KÝ HỢP LỆ mà số tiền vẫn sai - lý do lệnh kiểm này tồn tại'

$ketQua += Assert-Case 'Sai số tiền (quên chia 100)' '04' `
    (Invoke-Ipn (New-ThamSo ($soTienVnPay * 100) "$OrderId") $secret) `
    'Lỗi nhân/chia 100 bị bắt'

$ketQua += Assert-Case 'Đơn không tồn tại' '01' `
    (Invoke-Ipn (New-ThamSo $soTienVnPay '999999999') $secret) `
    'vnp_TxnRef trỏ tới đơn không có thật'

$ketQua += Assert-Case 'Thành công' '00' `
    (Invoke-Ipn (New-ThamSo $soTienVnPay "$OrderId") $secret) `
    'Đơn chuyển sang Paid, tạo bản ghi Payment'

$ketQua += Assert-Case 'Gửi lại (idempotent)' '02' `
    (Invoke-Ipn (New-ThamSo $soTienVnPay "$OrderId") $secret) `
    'VNPay gửi lại khi chưa nhận được phản hồi'

$ketQua | Format-Table -AutoSize -Wrap

# ─────────────────────────── Đối chiếu DB ───────────────────────────

$sauCung = (Invoke-Sql @"
SELECT CONCAT(o.Status, '|', COUNT(p.Id), '|', ISNULL(MAX(CAST(p.Amount AS varchar(30))), '-'))
FROM Orders o LEFT JOIN Payments p ON p.OrderId = o.Id
WHERE o.Id = $OrderId GROUP BY o.Status
"@).Trim()

$status, $soBanGhi, $soTienDaGhi = $sauCung -split '\|'

Write-Host 'Trạng thái DB sau khi chạy:' -ForegroundColor Cyan
Write-Host ("  Orders.Status      = {0}  (mong đợi Paid)" -f $status)
Write-Host ("  Số bản ghi Payment = {0}  (mong đợi 1 - hai lần gọi nhưng chỉ một bản ghi)" -f $soBanGhi)
Write-Host ("  Payments.Amount    = {0}  (mong đợi {1})" -f $soTienDaGhi, $tongTien)

if ($status -ne 'Paid') { $script:soLoi++; Write-Host '  -> SAI: đơn chưa chuyển sang Paid' -ForegroundColor Red }
if ($soBanGhi -ne '1') { $script:soLoi++; Write-Host '  -> SAI: số bản ghi Payment phải đúng bằng 1' -ForegroundColor Red }

# ─────────────────────────── Dọn dẹp ───────────────────────────

if ($Reset) {
    Write-Host ''
    Write-Host "Reset: đưa đơn $OrderId về Pending và xoá bản ghi Payment của nó." -ForegroundColor Yellow

    Invoke-Sql "DELETE FROM Payments WHERE OrderId = $OrderId; UPDATE Orders SET Status = 'Pending' WHERE Id = $OrderId;" | Out-Null
}
else {
    Write-Host ''
    Write-Host "Đơn $OrderId giờ đang ở trạng thái Paid. Thêm -Reset để chạy lại được." -ForegroundColor DarkGray
}

Write-Host ''

if ($script:soLoi -gt 0) {
    Write-Host "$($script:soLoi) kiểm tra KHÔNG đạt." -ForegroundColor Red
    exit 1
}

Write-Host 'Tất cả các case đều đúng như mong đợi.' -ForegroundColor Green
exit 0
