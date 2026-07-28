// Dropdown giỏ hàng trên navbar: nút +/- và xoá, cập nhật tại chỗ không tải lại trang.
//
// Nguyên tắc xuyên suốt file này: JavaScript KHÔNG dựng markup và KHÔNG định dạng tiền.
//   - Đổi vài con số  -> gán textContent từ JSON (server đã định dạng sẵn tiền).
//   - Đổi cấu trúc    -> tải lại HTML dropdown từ GET /Cart/Dropdown (Razor dựng).
// Nhờ vậy markup thẻ sản phẩm và cách in tiền vẫn chỉ tồn tại một chỗ duy nhất.
(function () {
    'use strict';

    var container = document.querySelector('[data-cart-dropdown-container]');
    var badge = document.querySelector('[data-cart-count]');

    if (!container) {
        return;
    }

    // Chặn double-click: fetch là bất đồng bộ nên bấm "+" hai lần thật nhanh sẽ gửi hai
    // request cùng đọc số lượng cũ. Cờ đặt ở mức toàn dropdown chứ không theo từng dòng:
    // mọi thao tác đều làm tổng tiền đổi, nên hai thao tác song song vẫn đá nhau.
    var dangGui = false;

    // Dropdown chỉ cần dựng lại HTML khi CẤU TRÚC đổi (thêm dòng mới từ trang sản phẩm,
    // hoặc giỏ vừa trở thành rỗng). Đổi số lượng thì không cần.
    var canDungLai = false;

    document.addEventListener('click', function (event) {
        var nut = event.target.closest('[data-cart-action]');

        if (nut && container.contains(nut)) {
            event.preventDefault();
            xuLyThaoTac(nut);
            return;
        }

        // Nút "Thêm vào giỏ" ở thẻ sản phẩm nằm NGOÀI dropdown: nó có thể tạo dòng mới
        // nên chỉ cập nhật badge, rồi đánh dấu dropdown cần dựng lại.
        var formThem = event.target.closest('form[data-cart-add]');

        if (formThem) {
            event.preventDefault();
            themVaoGio(formThem);
        }
    });

    // Dựng lại dropdown ngay TRƯỚC khi nó hiện ra, không phải sau: 'show.bs.dropdown'
    // chạy trước khi menu hiển thị nên người dùng không thấy nội dung cũ nhấp nháy.
    var toggle = document.getElementById('cartDropdownToggle');

    if (toggle) {
        toggle.addEventListener('show.bs.dropdown', function () {
            if (canDungLai) {
                taiLaiDropdown();
            }
        });
    }

    function xuLyThaoTac(nut) {
        var dong = nut.closest('[data-cart-line]');
        var goc = nut.closest('[data-cart-dropdown]');

        if (!dong || !goc) {
            return;
        }

        var productId = dong.getAttribute('data-product-id');
        var thaoTac = nut.getAttribute('data-cart-action');

        if (thaoTac === 'remove') {
            gui(goc.getAttribute('data-url-remove'), { ProductId: productId }, goc);
            return;
        }

        var hienTai = parseInt(dong.querySelector('[data-cart-quantity]').textContent.trim(), 10);

        if (isNaN(hienTai)) {
            return;
        }

        // UpdateQuantity nhận số TUYỆT ĐỐI, nên client tự tính số mới. Quantity = 0
        // được server dịch thành xoá dòng - đó là lý do nút "−" ở số 1 không cần
        // xử lý riêng.
        var moi = thaoTac === 'increase' ? hienTai + 1 : hienTai - 1;

        if (moi < 0) {
            return;
        }

        gui(goc.getAttribute('data-url-update'), { ProductId: productId, Quantity: moi }, goc);
    }

    function themVaoGio(form) {
        var goc = container.querySelector('[data-cart-dropdown]');

        if (!goc) {
            return;
        }

        var duLieu = {};
        new FormData(form).forEach(function (value, key) {
            // Bỏ token của form đó: hàm gui() tự lấy token từ dropdown.
            if (key !== '__RequestVerificationToken') {
                duLieu[key] = value;
            }
        });

        gui(form.getAttribute('action'), duLieu, goc, true);
    }

    function gui(url, truong, goc, laThemMoi) {
        if (dangGui || !url) {
            return;
        }

        dangGui = true;

        var token = layToken(goc);
        var body = new URLSearchParams();

        Object.keys(truong).forEach(function (key) {
            body.set(key, truong[key]);
        });

        fetch(url, {
            method: 'POST',
            headers: {
                // Accept quyết định server trả JSON thay vì PartialView.
                'Accept': 'application/json',
                'Content-Type': 'application/x-www-form-urlencoded',
                // fetch() không tự gửi __RequestVerificationToken như form HTML.
                'RequestVerificationToken': token
            },
            body: body.toString()
        })
            .then(function (response) {
                // fetch KHÔNG reject khi server trả 4xx/5xx, chỉ reject khi lỗi mạng.
                // Bỏ bước này là đọc JSON của trang lỗi và ném ở chỗ chẳng liên quan.
                if (!response.ok) {
                    throw new Error('HTTP ' + response.status);
                }

                return response.json();
            })
            .then(function (ketQua) {
                apDung(ketQua, goc, laThemMoi);
            })
            .catch(function () {
                // Không có gì cứu được ở client: tải lại trang để trạng thái hiển thị
                // khớp lại với server, thay vì để người dùng nhìn số liệu sai.
                window.location.reload();
            })
            .finally(function () {
                dangGui = false;
            });
    }

    function apDung(ketQua, goc, laThemMoi) {
        capNhatBadge(ketQua.count);
        hienThongBao(goc, ketQua.notice);

        // Giỏ rỗng, hoặc vừa thêm một sản phẩm có thể chưa có dòng nào: cấu trúc đổi
        // nên phải để Razor dựng lại. JS không tạo thẻ HTML.
        if (ketQua.isEmpty || laThemMoi) {
            canDungLai = true;

            // Dropdown đang mở thì dựng lại ngay để người dùng thấy kết quả.
            if (container.classList.contains('show')) {
                taiLaiDropdown();
            }

            return;
        }

        var dong = goc.querySelector('[data-cart-line][data-product-id="' + ketQua.line.productId + '"]');

        if (ketQua.line.removed) {
            if (dong) {
                dong.remove();
            }

            // Dòng cuối vừa mất mà server chưa báo isEmpty (sản phẩm đã bị xoá khỏi
            // shop nên không tính vào giỏ) -> vẫn cần Razor dựng lại phần rỗng.
            if (!goc.querySelector('[data-cart-line]')) {
                canDungLai = true;
                taiLaiDropdown();
                return;
            }
        } else if (dong) {
            // CHỈ gán textContent, không innerHTML: dữ liệu tuy do server sinh nhưng
            // giữ đúng thói quen là rào chắn rẻ nhất chống XSS.
            dong.querySelector('[data-cart-quantity]').textContent = ketQua.line.quantity;
            dong.querySelector('[data-cart-line-total]').textContent = ketQua.line.lineTotalText;
        } else {
            // Dòng lẽ ra phải có mà không tìm thấy -> DOM đã lệch khỏi server.
            canDungLai = true;
            taiLaiDropdown();
            return;
        }

        // Tổng tiền do SERVER tính và định dạng. Cộng trừ ở client sẽ sai ngay khi
        // server kẹp số lượng theo tồn kho, và cách in tiền sẽ lệch theo locale máy.
        var tong = goc.querySelector('[data-cart-total]');

        if (tong) {
            tong.textContent = ketQua.totalText;
        }

        // Số lượng đổi thì badge trạng thái "Không đủ hàng" có thể đã khác -> để lần
        // mở sau dựng lại từ server cho chắc.
        if (ketQua.notice) {
            canDungLai = true;
        }
    }

    function capNhatBadge(soMon) {
        if (!badge) {
            return;
        }

        badge.textContent = soMon;

        // Badge luôn tồn tại trong DOM, chỉ ẩn/hiện. Server render sẵn node này chính
        // là để JS không phải tạo thẻ khi giỏ từ rỗng thành có hàng.
        if (soMon > 0) {
            badge.removeAttribute('hidden');
        } else {
            badge.setAttribute('hidden', 'hidden');
        }
    }

    function hienThongBao(goc, notice) {
        var o = goc.querySelector('[data-cart-notice]');

        if (!o) {
            return;
        }

        if (notice) {
            o.textContent = notice;
            o.removeAttribute('hidden');
        } else {
            o.textContent = '';
            o.setAttribute('hidden', 'hidden');
        }
    }

    function taiLaiDropdown() {
        var goc = container.querySelector('[data-cart-dropdown]');
        var url = goc && goc.getAttribute('data-url-dropdown');

        if (!url) {
            return;
        }

        // Thông báo hiện tại phải được giữ lại: HTML mới từ server không biết gì về nó.
        var oThongBao = goc.querySelector('[data-cart-notice]');
        var thongBaoDangHien = oThongBao && !oThongBao.hasAttribute('hidden')
            ? oThongBao.textContent
            : null;

        fetch(url, { headers: { 'X-Requested-With': 'XMLHttpRequest' } })
            .then(function (response) {
                if (!response.ok) {
                    throw new Error('HTTP ' + response.status);
                }

                return response.text();
            })
            .then(function (html) {
                // insertAdjacentHTML/innerHTML với HTML do Razor sinh: Razor đã escape ở
                // server, và cả hai đều KHÔNG thực thi thẻ <script> được chèn.
                container.innerHTML = html;
                canDungLai = false;

                var gocMoi = container.querySelector('[data-cart-dropdown]');

                if (gocMoi && thongBaoDangHien) {
                    hienThongBao(gocMoi, thongBaoDangHien);
                }
            })
            .catch(function () {
                // Không reload ở đây: dropdown cũ vẫn đọc được, và lần mở sau thử lại.
                canDungLai = true;
            });
    }

    function layToken(goc) {
        var o = goc.querySelector('input[name="__RequestVerificationToken"]');

        return o ? o.value : '';
    }
})();
