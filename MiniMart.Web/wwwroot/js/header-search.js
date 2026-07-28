// Gợi ý cho ô tìm kiếm ở header: gọi /Product/Suggest và đổ HTML trả về vào panel.
//
// Server trả HTML đã render sẵn (PartialView) chứ không phải JSON, nên file này KHÔNG
// dựng markup. Điều đó quan trọng hơn ở đây so với mọi chỗ khác trong dự án: thứ được
// chèn vào DOM là TÊN SẢN PHẨM. Razor escape ở server nên không thể quên; nếu trả JSON
// thì JSON không escape ký tự '<' và việc escape rơi vào chính file này.
//
// Toàn bộ file là lớp TĂNG CƯỜNG: form vẫn là form GET thật, tắt JavaScript thì mất
// gợi ý chứ không mất tìm kiếm.
(function () {
    'use strict';

    var form = document.querySelector('[data-tim-kiem]');

    if (!form) {
        return;
    }

    var o = form.querySelector('[data-o-tim-kiem]');
    var panel = form.querySelector('[data-bang-goi-y]');
    var url = form.getAttribute('data-url-goi-y');

    if (!o || !panel || !url) {
        return;
    }

    // Chờ người dùng ngừng gõ rồi mới gọi server. Không có nó thì "iphone" bắn SÁU
    // request, năm cái đầu vô nghĩa và cái trả về CUỐI CÙNG chưa chắc là cái mới nhất.
    var TRE_MS = 200;

    var boDem = null;
    var lanGoiHienTai = 0;
    var viTriChon = -1;

    o.addEventListener('input', function () {
        window.clearTimeout(boDem);
        boDem = window.setTimeout(taiGoiY, TRE_MS);
    });

    // Quay lại ô nhập thì hiện lại kết quả cũ, không phải gọi server lần nữa.
    o.addEventListener('focus', function () {
        if (panel.children.length > 0) {
            hien();
        }
    });

    o.addEventListener('keydown', function (event) {
        if (event.key === 'Escape') {
            an();
            return;
        }

        var dong = panel.querySelectorAll('[data-goi-y]');

        if (dong.length === 0 || panel.hasAttribute('hidden')) {
            return;
        }

        if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
            // preventDefault để con trỏ text không nhảy về đầu/cuối ô nhập.
            event.preventDefault();

            var buoc = event.key === 'ArrowDown' ? 1 : -1;

            // Cộng thêm length trước khi chia dư: JavaScript cho -1 % 8 = -1 chứ
            // không phải 7, nên thiếu bước này thì mũi tên lên ở dòng đầu ra chỉ số âm.
            viTriChon = (viTriChon + buoc + dong.length) % dong.length;
            veLuaChon(dong);
            return;
        }

        if (event.key === 'Enter' && viTriChon >= 0) {
            // Chỉ chặn submit khi người dùng ĐANG chọn một gợi ý bằng bàn phím. Không
            // chọn gì thì Enter phải submit form như bình thường - đó là đường tìm
            // kiếm đầy đủ, và cướp nó đi là làm hỏng thứ vốn chạy được.
            event.preventDefault();
            dong[viTriChon].click();
        }
    });

    // Bấm ra ngoài thì đóng. Dùng 'mousedown' chứ không 'click': click xảy ra SAU khi
    // ô nhập mất focus, và với một số trình duyệt thì panel đã kịp đóng nên cú bấm vào
    // chính dòng gợi ý bị rơi vào khoảng không.
    document.addEventListener('mousedown', function (event) {
        if (!form.contains(event.target)) {
            an();
        }
    });

    function taiGoiY() {
        var tuKhoa = o.value.trim();

        // Ngưỡng này phải KHỚP với DoDaiToiThieu ở ProductRepository. Client kiểm để
        // khỏi gọi mạng vô ích; server vẫn kiểm lại vì mọi thứ đến từ client đều sửa được.
        if (tuKhoa.length < 2) {
            an();
            panel.innerHTML = '';
            return;
        }

        // Mỗi lần gọi mang một số thứ tự. Trả lời của một request CŨ về muộn hơn
        // request mới sẽ bị bỏ qua - nếu không, gõ nhanh "iph" rồi "iphone" có thể
        // kết thúc bằng việc hiển thị kết quả của "iph".
        var lanGoi = ++lanGoiHienTai;

        fetch(url + '?tuKhoa=' + encodeURIComponent(tuKhoa), {
            headers: { 'Accept': 'text/html' }
        })
            .then(function (response) {
                // fetch KHÔNG reject khi server trả 4xx/5xx, chỉ reject khi lỗi mạng.
                if (!response.ok) {
                    throw new Error('HTTP ' + response.status);
                }

                return response.text();
            })
            .then(function (html) {
                if (lanGoi !== lanGoiHienTai) {
                    return;
                }

                // insertAdjacentHTML/innerHTML với HTML do Razor sinh: đã escape ở
                // server, và cả hai đều KHÔNG thực thi thẻ <script> được chèn.
                panel.innerHTML = html;
                viTriChon = -1;

                if (panel.children.length > 0) {
                    hien();
                } else {
                    an();
                }
            })
            .catch(function () {
                // Gợi ý hỏng KHÔNG được làm hỏng ô tìm kiếm: đóng panel và để người
                // dùng bấm Enter đi tiếp như khi không có JavaScript.
                an();
            });
    }

    function veLuaChon(dong) {
        for (var i = 0; i < dong.length; i++) {
            if (i === viTriChon) {
                dong[i].classList.add('es-suggest-active');
                dong[i].setAttribute('aria-selected', 'true');

                // Giữ dòng đang chọn trong tầm nhìn khi danh sách dài hơn panel.
                dong[i].scrollIntoView({ block: 'nearest' });
            } else {
                dong[i].classList.remove('es-suggest-active');
                dong[i].removeAttribute('aria-selected');
            }
        }
    }

    function hien() {
        panel.removeAttribute('hidden');
        o.setAttribute('aria-expanded', 'true');
    }

    function an() {
        panel.setAttribute('hidden', 'hidden');
        o.setAttribute('aria-expanded', 'false');
        viTriChon = -1;
    }
})();
