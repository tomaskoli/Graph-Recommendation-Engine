/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { RecommendationDto } from '../models/RecommendationDto';
import type { CancelablePromise } from '../core/CancelablePromise';
import { OpenAPI } from '../core/OpenAPI';
import { request as __request } from '../core/request';
export class RecommendationsService {
    /**
     * @returns RecommendationDto OK
     * @throws ApiError
     */
    public static getRecommendations({
        productId,
        page,
        pageSize,
        strategy,
    }: {
        productId: number | string,
        page: number | string,
        pageSize: number | string,
        strategy?: string,
    }): CancelablePromise<RecommendationDto> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/recommendations',
            query: {
                'productId': productId,
                'page': page,
                'pageSize': pageSize,
                'strategy': strategy,
            },
            errors: {
                400: `Bad Request`,
            },
        });
    }
}
