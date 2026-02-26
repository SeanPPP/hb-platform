using System.Threading.Tasks;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services.Pricing;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers.React
{
    [ApiController]
    [Route("api/react/v1/pricing-strategies")]
    [Authorize]
    public class ReactPricingStrategiesController : ControllerBase
    {
        private readonly IPricingStrategyReactService _service;
        private readonly AutoPricingService _autoPricing;

        public ReactPricingStrategiesController(
            IPricingStrategyReactService service,
            AutoPricingService autoPricing
        )
        {
            _service = service;
            _autoPricing = autoPricing;
        }

        [HttpPost("grid")]
        public async Task<ActionResult<GridResponseDto<PricingStrategyListDto>>> Grid(
            [FromBody] GridRequestDto request
        )
        {
            var res = await _service.GetGridAsync(request);
            return Ok(res);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<ApiResponse<PricingStrategyDetailDto>>> Get(string id)
        {
            var res = await _service.GetByIdAsync(id);
            return Ok(res);
        }

        [HttpPost]
        public async Task<ActionResult<ApiResponse<PricingStrategyDetailDto>>> Create(
            [FromBody] CreatePricingStrategyDto dto
        )
        {
            var res = await _service.CreateAsync(dto);
            return Ok(res);
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<ApiResponse<PricingStrategyDetailDto>>> Update(
            string id,
            [FromBody] UpdatePricingStrategyDto dto
        )
        {
            var res = await _service.UpdateAsync(id, dto);
            return Ok(res);
        }

        [HttpDelete("{id}")]
        public async Task<ActionResult<ApiResponse<bool>>> Delete(string id)
        {
            var res = await _service.DeleteAsync(id);
            return Ok(res);
        }

        [HttpPost("evaluate")]
        public async Task<ActionResult<ApiResponse<PricingEvaluateResponse>>> Evaluate(
            [FromBody] PricingEvaluateRequest req
        )
        {
            if (req.PurchasePrice <= 0)
            {
                return Ok(ApiResponse<PricingEvaluateResponse>.Error("进货价必须大于0"));
            }

            var strategy = await _autoPricing.FindStrategyForPriceAsync(
                req.PurchasePrice,
                req.SupplierCode,
                req.StoreCode
            );
            var retail = _autoPricing.CalculateRetailPrice(req.PurchasePrice, strategy);
            var rate = _autoPricing.CalculateRate(req.PurchasePrice, strategy);

            PricingEvaluateRuleInfo? ruleInfo = null;
            if (strategy?.Details != null)
            {
                var rule = strategy.Details.FirstOrDefault(d =>
                    req.PurchasePrice >= d.MinPrice && req.PurchasePrice <= d.MaxPrice
                );
                if (rule != null)
                {
                    ruleInfo = new PricingEvaluateRuleInfo
                    {
                        MinPrice = rule.MinPrice,
                        MaxPrice = rule.MaxPrice,
                        Algorithm = rule.Algorithm,
                        StartRate = rule.StartRate,
                        EndRate = rule.EndRate,
                    };
                }
            }

            var resp = new PricingEvaluateResponse
            {
                RetailPrice = retail,
                Rate = rate,
                StrategyId = strategy?.Id,
                Rule = ruleInfo,
            };
            return Ok(ApiResponse<PricingEvaluateResponse>.OK(resp));
        }
    }
}
