// Nút "Xem thêm" trên trang chủ: gọi /Product/LoadMore và dán HTML trả về vào
// cuối lưới sản phẩm, giữ nguyên bộ lọc đang áp dụng.
//
// Server trả về HTML đã render sẵn (PartialView) chứ không phải JSON, nên file
// này KHÔNG dựng markup. Toàn bộ giao diện thẻ sản phẩm nằm ở
// Views/Shared/_ProductCard.cshtml - đúng một nơi duy nhất.
(function () {
    'use strict';

    const button = document.getElementById('btnLoadMore');

    // Nút chỉ được render khi còn trang sau. Không có nút thì không có gì để làm -
    // thoát sớm thay vì để các getElementById phía dưới trả null rồi nổ.
    if (!button) {
        return;
    }

    const grid = document.getElementById('productGrid');
    const shownCount = document.getElementById('shownCount');
    const errorBox = document.getElementById('loadMoreError');

    // Chặn double-click: fetch là bất đồng bộ nên bấm hai lần nhanh sẽ gửi HAI
    // request cùng một page, và trang đó bị dán vào lưới hai lần.
    let dangTai = false;

    function buildUrl(page) {
        const params = new URLSearchParams();
        params.set('page', page);

        // Filter nào KHÔNG có giá trị thì bỏ hẳn khỏi query string, chứ không gửi
        // lên chuỗi rỗng để server tự đoán. Đây là bản sao phía client của chuỗi
        // `if (x.HasValue) query = query.Where(...)` trong ProductRepository.
        const filters = {
            categoryId: button.dataset.categoryId,
            minPrice: button.dataset.minPrice,
            maxPrice: button.dataset.maxPrice
        };

        for (const [key, value] of Object.entries(filters)) {
            if (value) {
                params.set(key, value);
            }
        }

        return button.dataset.url + '?' + params.toString();
    }

    button.addEventListener('click', async function () {
        if (dangTai) {
            return;
        }

        dangTai = true;
        button.disabled = true;
        errorBox.classList.add('d-none');

        try {
            const response = await fetch(buildUrl(button.dataset.nextPage), {
                headers: { 'Accept': 'text/html' }
            });

            // fetch KHÔNG reject khi server trả 500 - chỉ reject khi lỗi mạng.
            // Không kiểm tra response.ok thì HTML lỗi sẽ được dán thẳng vào lưới.
            if (!response.ok) {
                throw new Error('HTTP ' + response.status);
            }

            const html = await response.text();
            const soTheTruoc = grid.children.length;

            // Dán HTML do Razor render. An toàn vì mọi dữ liệu đã được Razor
            // escape ở server; và insertAdjacentHTML không thực thi thẻ <script>
            // trong chuỗi được chèn. Đây chính là cái mà cách trả JSON không có:
            // ở đó việc escape là trách nhiệm của JavaScript, quên một trường là XSS.
            grid.insertAdjacentHTML('beforeend', html);

            // Đếm theo DOM thay vì bắt server gửi thêm một header số lượng -
            // số thẻ thực sự đã vào lưới là con số đáng tin hơn.
            shownCount.textContent =
                Number(shownCount.textContent) + (grid.children.length - soTheTruoc);

            // Server là nơi duy nhất biết còn trang sau hay không. Header rỗng
            // nghĩa là hết dữ liệu -> bỏ nút hẳn, không để nút bấm được mà không ra gì.
            const trangKe = response.headers.get('X-Next-Page');
            if (trangKe) {
                button.dataset.nextPage = trangKe;
                button.disabled = false;
            } else {
                button.remove();
            }
        } catch (error) {
            errorBox.textContent = 'Không tải được thêm sản phẩm. Vui lòng thử lại.';
            errorBox.classList.remove('d-none');
            button.disabled = false;
            console.error('LoadMore that bai:', error);
        } finally {
            dangTai = false;
        }
    });
})();
